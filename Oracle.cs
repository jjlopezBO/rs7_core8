using System;
using System.Data;
using Oracle.ManagedDataAccess.Client;
using NLog;

namespace ReadSpectrum7
{
    public sealed class OracleI
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private OracleConnection connection = null;
        private OracleTransaction transaction = null;
        private string connectionString = null;

        public OracleI()
        {
            logger.Info("ORACLE. ACCESO CONTROLADO MODULO N. 1.00.00");
        }

        public string ConnectionString
        {
            get { return connectionString; }
            set
            {
                connectionString = value;
                StartConnection();
            }
        }

        public void DisposeCommand(OracleCommand command)
        {
            if (command != null)
            {
                command.Dispose();
                logger.Debug("OracleCommand liberado correctamente.");
            }
        }

        public void EndConnection()
        {
            try
            {
                if (transaction != null)
                {
                    transaction.Dispose();
                    transaction = null;
                    logger.Info("Transacción finalizada y liberada.");
                }

                if (connection != null && connection.State == ConnectionState.Open)
                {
                    connection.Close();
                    connection.Dispose();
                    connection = null;
                    logger.Info("Conexión Oracle cerrada y liberada.");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error al cerrar la conexión Oracle.");
            }
        }

        public void StartConnection()
        {
            if (connection != null && connection.State == ConnectionState.Open)
                return;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                logger.Error("No se ha definido una cadena de conexión.");
                throw new InvalidOperationException("No se ha definido cadena de conexión.");
            }

            try
            {
                logger.Info("Iniciando conexión Oracle...");
                connection = new OracleConnection(connectionString);
                connection.Open();
                logger.Info("Conexión Oracle abierta correctamente.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error al abrir la conexión Oracle.");
            }
        }

        public void StartTransaction()
        {
            try
            {
                logger.Info("Iniciando transacción...");
                transaction = connection.BeginTransaction();
                logger.Info("Transacción iniciada correctamente.");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error al iniciar la transacción.");
            }
        }

        public void RollBackTransaction()
        {
            try
            {
                if (transaction != null)
                {
                    transaction.Rollback();
                    logger.Warn("Transacción revertida.");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error al hacer rollback de la transacción.");
            }
        }

        public void CommitTransaction()
        {
            try
            {
                if (transaction != null)
                {
                    transaction.Commit();
                    logger.Info("Transacción confirmada correctamente.");
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error al confirmar la transacción.");
            }
        }
    }
}