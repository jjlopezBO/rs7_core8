using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using NLog;

namespace ReadSpectrum7
{
    public sealed class DataRawObjectId
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private static readonly object syncRoot = new object();
        private static Dictionary<string, decimal> _data = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        private static DataRawObjectId _instance;
        private static OracleConnection _oracleConnection;

        // Singleton seguro: requiere conexión válida
        public static DataRawObjectId GetInstance(OracleConnection cn)
        {
            if (_instance == null)
            {
                lock (syncRoot)
                {
                    if (_instance == null)
                    {
                        if (cn == null)
                        {
                            logger.Error("Conexión Oracle es nula en GetInstance.");
                            throw new ArgumentNullException(nameof(cn));
                        }

                        _oracleConnection = cn;
                        _instance = new DataRawObjectId();
                        _instance.LoadData(_oracleConnection);
                    }
                }
            }
            return _instance;
        }

        public static DataRawObjectId Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException("DataRawObjectId no ha sido inicializado. Use GetInstance(cn) antes.");
                }
                return _instance;
            }
        }

        private DataRawObjectId() { }

        private int Insertar (
           
    string b1,
    string b2,
    string b3,
    string el,
    string tipo,
    string type)
        {
            int rowsAffected = 0;

            try
            {
                using (OracleCommand cmd = _oracleConnection.CreateCommand())
                {
                    cmd.CommandText = @"
        INSERT INTO data_raw_objet_sp7 (
    b1, b2, b3, el, tipo, type, id_obj, aux
) VALUES (
    :b1, :b2, :b3, :el, :tipo, :type, data_raw_objet_sp7_ID_OBJ_seq.NEXTVAL, NULL
)";

                    cmd.Parameters.Add("b1", OracleDbType.Varchar2).Value = b1.Trim();
                    cmd.Parameters.Add("b2", OracleDbType.Varchar2).Value = b2.Trim();
                    cmd.Parameters.Add("b3", OracleDbType.Varchar2).Value = b3.Trim();
                    cmd.Parameters.Add("el", OracleDbType.Varchar2).Value = el.Trim();
                    cmd.Parameters.Add("tipo", OracleDbType.Varchar2).Value = tipo.Trim();
                    cmd.Parameters.Add("type", OracleDbType.Varchar2).Value =type.Trim();

                    // rowsAffected = cmd.ExecuteNonQuery();
                    logger.Info("{0} \t{1} \t {2} \t{3} \t{4} \t {5} \t{6} ", b1,b2,b3,el,type);
                    //logger.Info("Se insertaron {0} registros en data_raw_objet_sp7", rowsAffected);
                }
            }
            catch (OracleException ex)
            {
                logger.Error(ex, "Error al cargar datos desde Oracle en LoadData.");
            }
        



            return rowsAffected;

        }
        private void RecargarData(Record record)
        {
            try
            {
                using (OracleCommand cmd = _oracleConnection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                           trim( b1) || '*' ||trim( b2 )|| '*' || trim(b3) || '*' || trim(el) || '*' || trim(tipo) || '*' || trim(type) AS key,
                            id_obj AS value
                        FROM data_raw_objet_sp7  where b1 =:pb1 and b2= :pb2 and  b3=:pb3 and  el = :pel and  tipo=:ptipo and  type=:ptype
                        ORDER BY b1, b2, b3 ";

                    cmd.Parameters.Add("b1", OracleDbType.Varchar2).Value =record.b1;
                    cmd.Parameters.Add("b2", OracleDbType.Varchar2).Value = record.b2;
                    cmd.Parameters.Add("b3", OracleDbType.Varchar2).Value = record.b3;
                    cmd.Parameters.Add("el", OracleDbType.Varchar2).Value = record.el;
                    cmd.Parameters.Add("tipo", OracleDbType.Varchar2).Value = "MvMoment";
                    cmd.Parameters.Add("type", OracleDbType.Varchar2).Value = record.type;

                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        int count = 0;
                        while (reader.Read())
                        {
                            string key = reader["key"].ToString().ToUpperInvariant();
                            decimal value = Convert.ToDecimal(reader["value"]);
                            AddValue(key, value);
                            count++;
                        }
                        logger.Info("Se cargaron {0} entradas en el diccionario DataRawObjectId.", count);
                    }
                }
            }
            catch (OracleException ex)
            {
                logger.Error(ex, "Error al cargar datos desde Oracle en LoadData.");
            }
        }
        private void LoadData(OracleConnection cn)
        {
            try
            {
                using (OracleCommand cmd = cn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT 
                           trim( b1) || '*' ||trim( b2 )|| '*' || trim(b3) || '*' || trim(el) || '*' || trim(tipo) || '*' || trim(type) AS key,
                            id_obj AS value
                        FROM data_raw_objet_sp7 
                        ORDER BY b1, b2, b3";

                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        int count = 0;
                        while (reader.Read())
                        {
                            string key = reader["key"].ToString().ToUpperInvariant();
                            decimal value = Convert.ToDecimal(reader["value"]);
                            AddValue(key, value);
                            count++;
                        }
                        logger.Info("Se cargaron {0} entradas en el diccionario DataRawObjectId.", count);
                    }
                }
            }
            catch (OracleException ex)
            {
                logger.Error(ex, "Error al cargar datos desde Oracle en LoadData.");
            }
        }

        private static void AddValue(string key, decimal value)
        {
            if (!_data.ContainsKey(key))
            {
                _data.Add(key, value);
                logger.Debug("Agregado al diccionario: {0} => {1}", key, value);
            }
        }

        public decimal GetValue(string key, Record record)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                logger.Warn("Se intentó obtener un valor con clave nula o vacía.");
                return 0;
            }

            decimal result;
            if (!_data.TryGetValue(key.ToUpperInvariant(), out result))
            {

                if (Insertar(record.b1, record.b2, record.b3, record.el, "MvMoment", record.type) > 0)
                {
                    RecargarData(record);
                    if (!_data.TryGetValue(key.ToUpperInvariant(), out result))
                    {
                        logger.Warn("NO SE LOGRO OBTENER REGISTRO." + key);
                    }
                logger.Warn("Se intento insertar el registro faltante." +key);
            }
            }


            return result;
        }
    }
}