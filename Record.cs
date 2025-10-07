using NLog;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace ReadSpectrum7
{
    public class Record
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public string b1, b2, b3, el, in_, type;
        private decimal id;
        private DateTime date;
        public double[] datos;
        private string raw;
        private int records_count;

        public Record(string data, string type_rec, DateTime workday)
        {
            this.raw = data.Substring(0, data.LastIndexOf(','));
            this.date = workday;
            this.type = type_rec;
            this.Parse();
        }

        public string Type => type;

        public string HeaderString()
        {
            return $"{b1}-{b2}-{b3}-{el}-{in_}";
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder($"{id}-{b1}-{b2}-{b3}-{el}-{in_}-");
            foreach (double dato in datos)
                sb.Append($"{dato}-");
            return sb.ToString();
        }

        public bool IsValid => !b1.Contains("[");

        private int IntervalNum(DateTime date)
        {
            return date.Hour * 4 + (int)(date.Minute / 15.0);
        }

        private double sumValues()
        {
            return datos.Sum();
        }

        public void InsertarEnBloquesConBindArray(List<Record> registros, int intInicio, int numIntervalo, DateTime fechaTrabajo, OracleConnection cn, int tamanoBloque = 10)
        {
            var logger = LogManager.GetCurrentClassLogger();

            for (int i = 0; i < registros.Count; i += tamanoBloque)
            {
                var bloque = registros.Skip(i).Take(tamanoBloque).ToList();

                // cantidad de filas a insertar por record
                int filasPorRecord = numIntervalo;
                int totalFilas = bloque.Count * filasPorRecord;

                // Inicializar arrays de datos
                decimal[] ids = new decimal[totalFilas];
                DateTime[] fechas = new DateTime[totalFilas];
                DateTime[] fechas_sng = new DateTime[totalFilas];
                int[] intervalos = new int[totalFilas];
                double[] valores = new double[totalFilas];
                string[] tipos = new string[totalFilas];

                int index = 0;

                foreach (var record in bloque)
                {
                    if (!record.IsValid || record.GetNumRec < intInicio + numIntervalo)
                        continue;

                    var idField = typeof(Record).GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    decimal id = (decimal)idField.GetValue(record);

                    for (int j = 0; j < filasPorRecord; j++)
                    {
                        ids[index] = id;
                        fechas[index] = fechaTrabajo.Date.AddMinutes((intInicio + j) * 15);
                        fechas_sng[index] = fechaTrabajo.Date;
                        intervalos[index] = intInicio + j;
                        valores[index] = record.datos[intInicio + j];
                        tipos[index] = record.el;
                        index++;
                    }
                }

                if (index == 0)
                    continue;

                try
                {
                    OracleCommand command = cn.CreateCommand();
                    command.CommandText = $"INSERT INTO  {type} (id, fecha, valor, fecha_sng, intervalo, type) VALUES (:vid, :vfecha, :valor, :vfecha_sng, :vintervalo, :vtpye)";
                    command.ArrayBindCount = index;

                    command.Parameters.Add("vid", OracleDbType.Decimal).Value = ids;
                    command.Parameters.Add("vfecha", OracleDbType.Date).Value = fechas;
                    command.Parameters.Add("valor", OracleDbType.Double).Value = valores;
                    command.Parameters.Add("vfecha_sng", OracleDbType.Date).Value = fechas_sng;
                    command.Parameters.Add("vintervalo", OracleDbType.Int16).Value = intervalos;
                    command.Parameters.Add("vtpye", OracleDbType.Varchar2).Value = tipos;

                    int rowsInserted = command.ExecuteNonQuery();
                    logger.Info($"Insertadas {rowsInserted} filas en bloque de {bloque.Count} records.");
                }
                catch (OracleException ex)
                {
                    logger.Error(ex, $"Error al insertar bloque con {bloque.Count} registros.");
                }
            }
        }

        public double LoadDataRcd(int intInicio, int numIntervalo, DateTime Datav, OracleConnection cn)
        {
            if (id == Decimal.Zero)
                return -1.0;

            double total = this.sumValues();
            if (numIntervalo == 0)
                return total;

            OracleCommand command = cn.CreateCommand();
            command.CommandText = $"INSERT INTO {type} (id, fecha, valor, fecha_sng, intervalo, type) VALUES (:vid, :vfecha, :valor, :vfecha_sng, :vintervalo, :vtpye)";
            decimal[] ids = new decimal[numIntervalo];
            DateTime[] fechas = new DateTime[numIntervalo];
            DateTime[] fechas_sng = new DateTime[numIntervalo];
            int[] intervalos = new int[numIntervalo];
            double[] valores = new double[numIntervalo];
            string[] tipos = new string[numIntervalo];

            try
            {
                for (int i = 0; i < numIntervalo; i++)
                {
                    ids[i] = id;
                    fechas[i] = Datav.Date.AddMinutes((intInicio + i) * 15);
                    fechas_sng[i] = Datav.Date;
                    intervalos[i] = intInicio + i;
                    valores[i] = datos[intInicio + i];
                    tipos[i] = el;
                }

                command.ArrayBindCount = numIntervalo;
                command.Parameters.Add("vid", OracleDbType.Decimal).Value = ids;
                command.Parameters.Add("vfecha", OracleDbType.Date).Value = fechas;
                command.Parameters.Add("valor", OracleDbType.Double).Value = valores;
                command.Parameters.Add("vfecha_sng", OracleDbType.Date).Value = fechas_sng;
                command.Parameters.Add("vintervalo", OracleDbType.Int16).Value = intervalos;
                command.Parameters.Add("vtpye", OracleDbType.Varchar2).Value = tipos;

              int rowcount =  command.ExecuteNonQuery();
            }
            catch (OracleException ex)
            {
                logger.Error(ex, $"Error al insertar en {type} para ID: {id} en fecha: {Datav:dd.MM.yy}");
                logger.Error("SQL: {0}", command.CommandText);
                for (int i = 0; i < numIntervalo; i++)
                {
                    logger.Debug("{0}\t{1:dd.MM.yy}\t{2:dd.MM.yy hh:mm}\t{3}\t{4}",
                        ids[i], fechas[i], fechas_sng[i], intervalos[i], valores[i]);
                }
                return -1.0;
            }

            return total;
        }
        public string  GeneraKey()
        {
            if (string.IsNullOrEmpty(b3)) b3 = "-";
            string value = $"{b1}*{b2}*{b3}*{el}*{in_}*{type}";
            return value.ToUpperInvariant();
        }

        private decimal GetId()
        {
             
            return DataRawObjectId.Instance.GetValue(GeneraKey(), this);
        }

        public int GetNumRec => datos.Length;

        private bool Parse()
        {
            string[] parts = raw.Split(',');
            string separator = Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator;

            if (parts.Length < 7) return false;

            b1 = parts[0].Trim();
            b2 = parts[1].Trim();
            b3 = parts[2].Trim();
            el = parts[3].Trim();
            in_ = parts[4].Trim();
            records_count = parts.Length - 5;
            datos = new double[records_count];

            id = GetId();

            if (id == Decimal.Zero && !b1.Contains("["))
            {
                logger.Warn("ID no encontrado para combinación: b1={0}, b2={1}, b3={2}", b1, b2, b3);
            }

            for (int i = 0; i < records_count; i++)
            {
                double val;
                datos[i] = double.TryParse(parts[5 + i].Replace(".", separator), out val) ? val : 0.0;
            }

            return true;
        }
    }
}