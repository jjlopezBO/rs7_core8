using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using NLog;

namespace ReadSpectrum7
{
    public class RecordBulkInserter
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private readonly List<Record> registros;
        private readonly DateTime fecha;
        private readonly int inicioIntervalo;
        private readonly int cantidadIntervalos;
        private readonly OracleConnection conexion;
        private const string tablaDestino = "TDM_TD30_BULK";
        private const int tamanioBloque = 50000;

        public RecordBulkInserter(List<Record> registros, DateTime fecha, int inicioIntervalo, int cantidadIntervalos, OracleConnection conexion)
        {
            this.registros = registros;
            this.fecha = fecha;
            this.inicioIntervalo = inicioIntervalo;
            this.cantidadIntervalos = cantidadIntervalos;
            this.conexion = conexion;
        }

        public int Cargar(  )
        {
            int rowsAffected = 0;

            try
            {
                using (OracleCommand cmd = conexion.CreateCommand())
                {
                    cmd.CommandText = @"
       INSERT INTO TDM_TD30_SP7 (ID, FECHA, VALOR, FECHA_SNG, INTERVALO, TYPE, FECHA_CARGA)
SELECT 
    ID,
    MIN(FECHA) AS FECHA,
    AVG(VALOR) AS VALOR,  -- o MIN(VALOR), MAX(VALOR)
    FECHA_SNG,
    INTERVALO,
    TYPE,
    SYSDATE
FROM TDM_TD30_BULK b
WHERE NOT EXISTS (
    SELECT 1 FROM TDM_TD30_SP7 s
    WHERE s.ID = b.ID
      AND s.FECHA_SNG = b.FECHA_SNG
      AND s.INTERVALO = b.INTERVALO
      AND s.TYPE = b.TYPE
)
GROUP BY ID, FECHA_SNG, INTERVALO, TYPE
";

                    

                      rowsAffected = cmd.ExecuteNonQuery();
                    
                }
            }
            catch (OracleException ex)
            {
                logger.Error(ex, "Error al cargar datos desde Oracle en LoadData.");
            }




            return rowsAffected;

        }

        public int Borrar()
        {
            int rowsAffected = 0;

            try
            {
                using (OracleCommand cmd = conexion.CreateCommand())
                {
                    cmd.CommandText = @"
     delete from TDM_TD30_BULK   ";



                    rowsAffected = cmd.ExecuteNonQuery();

                }
            }
            catch (OracleException ex)
            {
                logger.Error(ex, "Error al cargar datos desde Oracle en LoadData.");
            }




            return rowsAffected;

        }
        public void Insertar()
        {
            Stopwatch sw = Stopwatch.StartNew();

            logger.Info("Iniciando carga de registros masiva...");

            DataTable tabla = CrearDataTable();

            foreach (var record in registros)
            {
                if (!record.IsValid) continue;

                decimal recordId = DataRawObjectId.Instance.GetValue(record.GeneraKey(), record);
                if (recordId == 0) continue;

                if (record.GetNumRec < (inicioIntervalo + cantidadIntervalos))
                    continue;

                for (int i = 0; i < cantidadIntervalos; i++)
                {
                    tabla.Rows.Add(
                        recordId,
                        fecha.Date.AddMinutes((inicioIntervalo + i) * 15),
                        record.datos[inicioIntervalo + i],
                        fecha.Date,
                        inicioIntervalo + i,
                        record.el
                    );
                }
            }

            logger.Info("Se generaron {0} filas para insertar en {1}", tabla.Rows.Count, tablaDestino);

            InsertarPorBloques(tabla);

            sw.Stop();
            logger.Info("Carga completada en {0} segundos", sw.Elapsed.TotalSeconds.ToString("F2"));
        }

        private DataTable CrearDataTable()
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("ID", typeof(decimal));
            dt.Columns.Add("FECHA", typeof(DateTime));
            dt.Columns.Add("VALOR", typeof(double));
            dt.Columns.Add("FECHA_SNG", typeof(DateTime));
            dt.Columns.Add("INTERVALO", typeof(int));
            dt.Columns.Add("TYPE", typeof(string));
            return dt;
        }

        private void InsertarPorBloques(DataTable tabla)
        {
            int totalFilas = tabla.Rows.Count;
            int bloques = (int)Math.Ceiling(totalFilas / (double)tamanioBloque);

            for (int b = 0; b < bloques; b++)
            {
                int desde = b * tamanioBloque;
                int hasta = Math.Min(desde + tamanioBloque, totalFilas);

                DataTable bloque = tabla.Clone();
                for (int i = desde; i < hasta; i++)
                    bloque.ImportRow(tabla.Rows[i]);

                try
                {
                    using (OracleBulkCopy bulkCopy = new OracleBulkCopy(conexion))
                    {
                        bulkCopy.DestinationTableName = tablaDestino;
                        bulkCopy.BatchSize = bloque.Rows.Count;
                        bulkCopy.BulkCopyTimeout = 300;

                        bulkCopy.ColumnMappings.Add("ID", "ID");
                        bulkCopy.ColumnMappings.Add("FECHA", "FECHA");
                        bulkCopy.ColumnMappings.Add("VALOR", "VALOR");
                        bulkCopy.ColumnMappings.Add("FECHA_SNG", "FECHA_SNG");
                        bulkCopy.ColumnMappings.Add("INTERVALO", "INTERVALO");
                        bulkCopy.ColumnMappings.Add("TYPE", "TYPE");

                        bulkCopy.WriteToServer(bloque);
                    }

                    logger.Info("Bloque {0}/{1} insertado con {2} filas", b + 1, bloques, bloque.Rows.Count);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error al insertar bloque {0}", b + 1);
                }
            }
        }
    }
}