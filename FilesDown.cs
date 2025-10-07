using Microsoft.Extensions.Configuration;
using NLog;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ReadSpectrum7
{
    internal class FilesDown
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private string FilePattern = "";
        private string ServerPath = "";
        public string FileType = "";
        public DateTime LastDateFromDb;
        public DateTime LastDateFromFile;
        private List<Record> ListaRegistro;

        public FilesDown(string server, string pattern, string type)
        {
            ServerPath = server;
            FilePattern = pattern;
            FileType = type;
        }
        string NormalizePath(string originalPath, IConfiguration config)
        {
            var windowsPrefix = config["Files:WindowsPrefix"];
            var linuxPrefix = config["Files:LinuxPrefix"];

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return originalPath.Replace(windowsPrefix, linuxPrefix);
            }
            else
            {
                return originalPath; // en Windows lo dejas igual
            }
        }

        public List<FilesDown> ReadFilesDown(OracleConnection cn, IConfiguration config)
        {
            var filesDownList = new List<FilesDown>();
            try
            {
                using (OracleCommand command = cn.CreateCommand())
                {
                    command.CommandText = @"SELECT file_pattern, server_path, type 
                                            FROM schedule_files_sp7 
                                            WHERE file_pattern LIKE 'YYYYMMDD-900%' 
                                            GROUP BY file_pattern, server_path, type";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var fd = new FilesDown(
                                reader["server_path"].ToString(),
                                reader["file_pattern"].ToString(),
                                reader["type"].ToString());

                           fd.ServerPath= fd.NormalizePath(fd.ServerPath, config);
                            logger.Log (LogLevel.Info,  string.Format("Server PATH:{0}", fd.ServerPath));
                            filesDownList.Add(fd);
                        }
                    }
                }
            }
            catch (OracleException ex)
            {
                logger.Error(ex, "Error leyendo archivos desde la base de datos.");

                logger.Error(ex, ex.ToString());
               
            }
            return filesDownList;
        }

        public int ReadLast15Min(DateTime fecha, OracleConnection cn)
        {
            try
            {
                using (var command = cn.CreateCommand())
                {
                    command.CommandText = $"SELECT NVL(MAX(intervalo), -1) FROM {FileType} WHERE fecha_sng = TRUNC(:p_fecha)";
                    command.Parameters.Add(new OracleParameter("p_fecha", fecha.Date));
                    return Convert.ToInt32((decimal)command.ExecuteScalar());
                }
            }
            catch (OracleException ex)
            {
                logger.Error(ex, "Error al leer el último intervalo del archivo.");
                 logger.Error(ex, ex.ToString());
                return -1;
            }
        }

        public double ReadFile(DateTime loadDate)
        {
            double total = 0.0;
            try
            {
                string fullPath = FullPath(loadDate);
                if (File.Exists(fullPath))
                {
                    string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");
                    File.Copy(fullPath, tempPath);

                    total = readFile(tempPath, loadDate);

                    File.Delete(tempPath);
                }
                else
                {
                    logger.Warn("Archivo no encontrado: {0}", fullPath);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Error leyendo archivo para {FileType} en fecha {loadDate:yyyy-MM-dd}");
                logger.Error(ex, ex.ToString());
                throw;
            }

            return total;
        }

        private string FullPath(DateTime date)
        {
            string resolvedPath = Path.Combine(ServerPath, FilePattern.Replace("YYYYMMDD", date.ToString("yyyyMMdd")));
            logger.Debug("Ruta generada: {0}", resolvedPath);
            return resolvedPath;
        }

        private double readFile(string path, DateTime date)
        {
            var lines = ReadContent(path);
            ListaRegistro = new List<Record>();
            double total = 0.0;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (i == 0)
                {
                    TimeSpan ts = ReadFromFileLast15(line);
                    LastDateFromFile = date + ts;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    double lineSum = SumValues(line);
                    if (lineSum != -1.0) total += lineSum;

                    var record = new Record(line, FileType, date);
                    ListaRegistro.Add(record);

                    if (i == 1)
                    {
                        int numRec = record.GetNumRec;
                        LastDateFromFile = date.AddMinutes(15 * numRec);
                    }
                }
            }

            return total;
        }

        private List<string> ReadContent(string path)
        {
            var result = new List<string>();
            using (StreamReader sr = new StreamReader(path))
            {
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    if (!string.IsNullOrEmpty(line) && !line.EndsWith(",")) line += ",";
                    result.Add(line);
                }
            }
            return result;
        }

        private TimeSpan ReadFromFileLast15(string s)
        {
            string timePart = s.Substring(s.Length - 9, 8);
            return new TimeSpan(
                int.Parse(timePart.Substring(0, 2)),
                int.Parse(timePart.Substring(3, 2)),
                int.Parse(timePart.Substring(6, 2)));
        }

        private double SumValues(string line)
        {
            double sum = 0.0;
            string[] values = line.Split(',');
            string decimalSeparator = Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator;

            for (int i = 5; i < values.Length; i++)
            {
                if (double.TryParse(values[i].Trim().Replace(".", decimalSeparator), out double val))
                {
                    sum += val;
                }
            }

            return sum;
        }

        public double LoadToDb(DateTime date, OracleConnection cn, bool fullLoad = false)
        {
            if (ListaRegistro == null || ListaRegistro.Count == 0)
                return 0.0;

            int lastInterval = ReadLast15Min(date, cn);
            int recordInterval = ListaRegistro[0].GetNumRec - 1;
            int countToInsert = lastInterval != -1 ? recordInterval - lastInterval : recordInterval;

            if (fullLoad)
                logger.Info("Iniciando carga de {0} registros para {1}", ListaRegistro.Count, FileType);
            double recorcount = 0; 
            double ix = 0;
            foreach (var record in ListaRegistro)
            {
                try
                {
                    ix = record.LoadDataRcd(lastInterval + 1, countToInsert, date, cn);
                    if (ix == -1.0)
                    {


                        logger.Warn("Error cargando registro: {0} {1}", record.HeaderString(), record.Type);
                        logger.Warn(record.HeaderString() + record.Type);
                    }
                    else
                    { recorcount += ix; }

                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error al cargar registro en BD.");
                }
            }

            logger.Info("Se cargaron {0} registros", recorcount);

            if (fullLoad)
                logger.Info("Carga finalizada para {0}", FileType);

            return 0.0;
        }


        public double LoadToDbTdm(DateTime date, OracleConnection cn, bool fullLoad = false)
        {
            double rtn = 0.0;

            if (ListaRegistro == null || ListaRegistro.Count == 0)
                return 0.0;

            int lastInterval = 0;//ReadLast15Min(date, cn);
            int recordInterval = ListaRegistro[0].GetNumRec - 1;
            int countToInsert = lastInterval != -1 ? recordInterval - lastInterval : recordInterval;

            if (fullLoad)
                logger.Info("Iniciando carga de {0} registros para {1}", ListaRegistro.Count, FileType);

            logger.Info("INICIANDO CARGA EN BD");
            var inserter = new RecordBulkInserter(ListaRegistro, date, lastInterval + 1, countToInsert, cn);
          inserter.Insertar();
            logger.Info("FIN CARGA EN BD");
            inserter.Cargar();
            inserter.Borrar();


            return rtn;
        }
        public double SumDay(DateTime date, OracleConnection cn)
        {
            try
            {
                using (OracleCommand command = cn.CreateCommand())
                {
                    command.CommandText = $"SELECT NVL(SUM(valor), 0) FROM {FileType} WHERE fecha_sng = TRUNC(:p_fecha)";
                    command.Parameters.Add(new OracleParameter("p_fecha", date.Date));
                    return Convert.ToDouble((decimal)command.ExecuteScalar());
                }
            }
            catch (OracleException ex)
            {
                logger.Error(ex, "Error al sumar valores diarios para {0}", FileType);
                logger.Error(ex, ex.ToString());
                return -1.0;
            }
        }

        public override string ToString()
        {
            return this.FileType ;
        }
    }
}