using Microsoft.Extensions.Configuration;
using NLog;
using Oracle.ManagedDataAccess.Client;
using ReadSpectrum7;
using System;
using System.Data;
using System.Globalization;
using System.IO;

class Bootstrap
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    // Mantener abierto hasta el final para conservar el candado
    private static FileStream? _appLock;

    static int Main(string[] args)
    {
        // Cultura por defecto (coincide con el parseo existente)
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("es-BO");
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("es-BO");

        try
        {
            // --- Inicializar NLog ---
            var nlogPath = Path.Combine(AppContext.BaseDirectory, "NLog.config");
            if (File.Exists(nlogPath))
                LogManager.Setup().LoadConfigurationFromFile(nlogPath);
            else
                LogManager.Setup().LoadConfiguration(builder =>
                {
                    // Config mínima por código si falta NLog.config
                    builder.ForLogger().WriteToConsole();
                });

            // ====== SINGLETON LOCK (archivo) ======
            string lockPath = "/run/rspectrum7/lock"; // con systemd: RuntimeDirectory=rspectrum7
            try
            {
                var dir = Path.GetDirectoryName(lockPath)!;
                Directory.CreateDirectory(dir);

                // Exclusión mutuamente excluyente a nivel SO
                _appLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                if (_appLock.Length == 0)
                {
                    _appLock.WriteByte(0);
                    _appLock.Flush(true);
                }

                logger.Info("Lock adquirido en {0}. Continuando ejecución.", lockPath);
            }
            catch (IOException)
            {
                logger.Warn("Otra instancia está en ejecución (lock en {0}). Saliendo.", lockPath);
                return 99; // ocupado
            }
            catch (UnauthorizedAccessException ex)
            {
                // Fallback si no hay permisos en /run (ejecución manual)
                logger.Warn(ex, "Sin permisos en {0}. Intentando fallback a /tmp/rspectrum7.lock", lockPath);
                lockPath = "/tmp/rspectrum7.lock";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
                    _appLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    if (_appLock.Length == 0) { _appLock.WriteByte(0); _appLock.Flush(true); }
                    logger.Info("Lock adquirido en {0}.", lockPath);
                }
                catch (Exception ex2)
                {
                    logger.Error(ex2, "No se pudo adquirir ningún lock. Saliendo.");
                    return 99;
                }
            }
            // ====== FIN SINGLETON LOCK ======

            // --- Carga de configuración (appsettings.json + variables de entorno) ---
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables(prefix: "RS7_")
                .Build();

            logger.Info("Inicio de ejecución .NET 8 (Linux-ready)");

            // --- Parseo de parámetros: días atrás ---
            if (!TryParseArgs(args, out DateTime fechaInicio, out DateTime fechaFin))
                return 2;

            var connString = config.GetConnectionString("Oracle") ?? "";
            if (string.IsNullOrWhiteSpace(connString))
            {
                logger.Error("No se encontró la cadena de conexión. Configure ConnectionStrings:Oracle en appsettings.json o variable 'RS7_ConnectionStrings__Oracle'.");
                return 3;
            }

            using (OracleConnection cn = new OracleConnection(connString))
            {
                try
                {
                    cn.Open();
                    var dataRawObjectId = DataRawObjectId.GetInstance(cn);
                    var listaFilesDown = new FilesDown("", "", "").ReadFilesDown(cn, config);

                    for (DateTime fecha = fechaInicio; fecha <= fechaFin; fecha = fecha.AddDays(1))
                    {
                        foreach (FilesDown fd in listaFilesDown)
                        {
                            try
                            {
                                bool fullLog = (fd.FileType == "TDM_TD30_SP7");
                                logger.Info("Inicio de Proceso: {0} | fullLog: {1} | Hora: {2:HH:mm:ss}", fd.FileType, fullLog, DateTime.Now);

                                ProcessDay(fd, fecha, cn, fullLog);

                                logger.Info("Fin de Proceso: {0} | Hora: {1:HH:mm:ss}", fd.FileType, DateTime.Now);
                            }
                            catch (Exception exFd)
                            {
                                logger.Error(exFd, "Error en el procesamiento del archivo {0}", fd.FileType);
                            }
                        }

                        logger.Info("Cargando Oracle: Fecha: {0:dd/MM/yyyy} | Hora: {1:HH:mm:ss}", fecha, DateTime.Now);
                        CargarOracle(fecha, cn);
                        logger.Info("Fin Oracle: Fecha: {0:dd/MM/yyyy} | Hora: {1:HH:mm:ss}", fecha, DateTime.Now);
                    }
                }
                catch (Exception exMain)
                {
                    logger.Fatal(exMain, "Error crítico en la ejecución principal.");
                    return 4;
                }
            }

            logger.Info("Fin de ejecución.");
            return 0;
        }
        catch (Exception bootEx)
        {
            try { logger.Fatal(bootEx, "Fallo en bootstrap."); } catch { /* ignorar */ }
            return 1;
        }
        finally
        {
            // Liberar lock
            try { _appLock?.Dispose(); } catch { /* ignore */ }

            // Importante en consola/servicio para vaciar buffers de archivo
            LogManager.Shutdown();
        }
    }

    /// <summary>
    /// Parámetros:
    ///  - Sin args  -> hoy
    ///  - 1 arg N   -> solo N días atrás (0=hoy, 1=ayer)
    ///  - 2 args A B-> rango desde A días atrás hasta B días atrás (inclusive). Orden autoajustable.
    /// </summary>
    private static bool TryParseArgs(string[] args, out DateTime fechaInicio, out DateTime fechaFin)
    {
        fechaInicio = DateTime.Today;
        fechaFin = DateTime.Today;

        try
        {
            if (args.Length == 0)
            {
                LogManager.GetCurrentClassLogger().Info("Sin parámetros: se procesará la fecha de hoy ({0:dd/MM/yyyy})", fechaInicio);
                return true;
            }

            if (args.Length == 1)
            {
                if (int.TryParse(args[0], out int diasAtras))
                {
                    fechaInicio = DateTime.Today.AddDays(-diasAtras);
                    fechaFin = fechaInicio;
                    LogManager.GetCurrentClassLogger().Info("Procesando un solo día: {0:dd/MM/yyyy}", fechaInicio);
                    return true;
                }
                else
                {
                    LogManager.GetCurrentClassLogger().Error("Parámetro inválido: {0}. Debe ser un número entero (ej. 0, 1, 3).", args[0]);
                    return false;
                }
            }

            if (args.Length == 2)
            {
                if (int.TryParse(args[0], out int desde) && int.TryParse(args[1], out int hasta))
                {
                    fechaInicio = DateTime.Today.AddDays(-desde);
                    fechaFin = DateTime.Today.AddDays(-hasta);

                    if (fechaFin < fechaInicio)
                        (fechaInicio, fechaFin) = (fechaFin, fechaInicio);

                    LogManager.GetCurrentClassLogger().Info("Procesando rango de fechas: {0:dd/MM/yyyy} a {1:dd/MM/yyyy}", fechaInicio, fechaFin);
                    return true;
                }
                else
                {
                    LogManager.GetCurrentClassLogger().Error("Parámetros inválidos: {0} {1}. Ambos deben ser números enteros.", args[0], args[1]);
                    return false;
                }
            }

            LogManager.GetCurrentClassLogger().Error("Cantidad de parámetros incorrecta. Use:");
            LogManager.GetCurrentClassLogger().Error("  rspectrum7                → procesa hoy");
            LogManager.GetCurrentClassLogger().Error("  rspectrum7 1              → procesa ayer");
            LogManager.GetCurrentClassLogger().Error("  rspectrum7 3 1            → procesa desde hace 3 días hasta ayer");
            return false;
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Error(ex, "Error al interpretar los parámetros.");
            return false;
        }
    }

    private static void ProcessDay(FilesDown fd, DateTime fecha, OracleConnection cn, bool fullLog = false)
    {
        if (fullLog)
        {
            var l = LogManager.GetCurrentClassLogger();
            l.Info("Iniciando procesamiento: {0:yyyy-MM-dd} | Hora: {1:HH:mm:ss}", fecha, DateTime.Now);
        }

        double fileValue = fd.ReadFile(fecha);
        if (fullLog) LogManager.GetCurrentClassLogger().Info("\tFin ReadFile");

        if (fd.FileType == "TDM_TD30_SP7")
        {
            fd.LoadToDbTdm(fecha, cn, fullLog);
            if (fullLog) LogManager.GetCurrentClassLogger().Info("\tFin LoadToDb");
        }
        else
        {
            fd.LoadToDb(fecha, cn, fullLog);
            if (fullLog) LogManager.GetCurrentClassLogger().Info("\tFin LoadToDb");
        }

        double dbValue = fd.SumDay(fecha, cn);

        if (Math.Round(fileValue, 2) != Math.Round(dbValue, 2))
        {
            LogManager.GetCurrentClassLogger().Warn(
                "Valores diferentes para fecha {0:dd/MM/yyyy} - DB: {1} | File: {2}",
                fecha,
                dbValue.ToString("F2").Replace(",", "."),
                fileValue.ToString("F2").Replace(",", ".")
            );
        }
    }

    public static bool CargarOracle(DateTime fecha, OracleConnection cn)
    {
        try
        {
            using (var cmd = cn.CreateCommand())
            {
                cmd.BindByName = true;
                cmd.CommandText = "cargadatos_appmovl"; // añade el esquema si aplica: SCHEMA.cargadatos_appmovl
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 0; // si puede tardar mucho

                cmd.Parameters.Add("fecha", OracleDbType.Date).Value = fecha.Date;

                cmd.ExecuteNonQuery();
                logger.Info("Carga Oracle sin errores.");
                return true;
            }
        }
        catch (OracleException ex)
        {
            logger.Error(ex, "Error al ejecutar cargadatos_appmovl");
            return false;
        }
    }
}
x\