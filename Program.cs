using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Configuration;
using NLog;
using Oracle.ManagedDataAccess.Client;
using ReadSpectrum7;

class Bootstrap
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    static int Main(string[] args)
    {
        // Cultura por defecto (coincide con el parseo existente)
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("es-BO");
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("es-BO");

        // Inicializar NLog desde archivo (si no existe, no rompe)
        try
        {
            var nlogPath = Path.Combine(AppContext.BaseDirectory, "NLog.config");
            if (File.Exists(nlogPath))
                LogManager.Setup().LoadConfigurationFromFile(nlogPath);
            else
                LogManager.Setup().LoadConfiguration(builder =>
                {
                    // Config mínima por código si falta NLog.config
                    builder.ForLogger().WriteToConsole();
                });

            // Carga de configuración (appsettings.json + variables de entorno)
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables(prefix: "RS7_")
                .Build();

            logger.Info("Inicio de ejecución .NET 8 (Linux-ready)");

            // Fechas por argumentos (comportamiento original)
            if (!TryParseArgs(args, out DateTime fechaInicio, out DateTime fechaFin))
                return 2;

            var connString = config.GetConnectionString("Oracle") ?? "";
            if (string.IsNullOrWhiteSpace(connString))
            {
                logger.Error("No se encontró la cadena de conexión. Configure ConnectionStrings:Oracle en appsettings.json o variable 'RS7_ConnectionStrings__Oracle'.");
                return 3;
            }
            /*fechaInicio =DateTime.Now.Date.AddDays(-1);
            fechaFin = fechaInicio;*/

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
                        fecha= fecha.AddDays(1);
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
            // Si algo falla incluso antes de tener logger listo
            try { logger.Fatal(bootEx, "Fallo en bootstrap."); } catch { /* ignorar */ }
            return 1;
        }
        finally
        {
            // Importante en consola/servicio para vaciar buffers de archivo
            LogManager.Shutdown();
        }
    }

    // ... (tus métodos TryParseArgs y ProcessDay sin cambios)
    // (Déjalos tal como los pegaste)
    private static bool TryParseArgs(string[] args, out DateTime fecha, out DateTime fecha2)
    {
        fecha = DateTime.Today;
        fecha2 = DateTime.Today;

        if (args.Length == 1 && args[0] == "-1")
        {
            fecha = DateTime.Now.Date.AddDays(-1);
            fecha2 = DateTime.Now.Date.AddDays(-1);
            return true;
        }

        if (args.Length == 2)
        {
            try
            {
                CultureInfo culture = new CultureInfo("es-BO");
                fecha = DateTime.Parse(args[0], culture);
                fecha2 = DateTime.Parse(args[1], culture);
                return true;
            }
            catch (Exception ex)
            {
                var logger = LogManager.GetCurrentClassLogger();
                logger.Error(ex, "Formato incorrecto. Use: dd/MM/yyyy. Ej: 01/06/2025");
                return false;
            }
        }

        var l = LogManager.GetCurrentClassLogger();
        l.Warn("No se proporcionaron parámetros de fecha. Se usa la fecha actual.");
        return true;
    }

    private static void ProcessDay(FilesDown fd, DateTime fecha, OracleConnection cn, bool fullLog = false)
    {
        if (fullLog)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logger.Info("Iniciando procesamiento: {0:yyyy-MM-dd} | Hora: {1:HH:mm:ss}", fecha, DateTime.Now);
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
            LogManager.GetCurrentClassLogger().Warn("Valores diferentes para fecha {0:dd/MM/yyyy} - DB: {1} | File: {2}",
                fecha,
                dbValue.ToString("F2").Replace(",", "."),
                fileValue.ToString("F2").Replace(",", "."));
        }
    }
}