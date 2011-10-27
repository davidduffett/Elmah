#region License, Terms and Author(s)
//
// ELMAH - Error Logging Modules and Handlers for ASP.NET
// Copyright (c) 2004-9 Atif Aziz. All rights reserved.
//
//  Author(s):
//
//      Atif Aziz, http://www.raboof.com
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

// All code in this file requires .NET Framework 2.0 or later.

#if !NET_1_1 && !NET_1_0

[assembly: Elmah.Scc("$Id: SqlCompactErrorLog.cs 776 2011-01-12 21:09:24Z azizatif $")]

namespace Elmah
{
    #region Imports

    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.SqlServerCe;
    using System.IO;

    using IDictionary = System.Collections.IDictionary;

    #endregion

    /// <summary>
    /// An Elmah <see cref="ErrorLog"/> implementation that uses SQL Server Compact 4 as its backing store.
    /// </summary>

    public class SqlCompactErrorLog : ErrorLog
    {
        private readonly string _connectionString;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlCompactErrorLog"/> class
        /// using a dictionary of configured settings.
        /// </summary>

        public SqlCompactErrorLog(IDictionary config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            string connectionString = ConnectionStringHelper.GetConnectionString(config);

            //
            // If there is no connection string to use then throw an 
            // exception to abort construction.
            //

            if (connectionString.Length == 0)
                throw new Elmah.ApplicationException("Connection string is missing for the SQL Server Compact error log.");

            _connectionString = connectionString;

            InitializeDatabase();

            if (config.Contains("applicationName") && !string.IsNullOrEmpty(config["applicationName"].ToString()))
            {
                ApplicationName = config["applicationName"].ToString();
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlCompactErrorLog"/> class
        /// to use a specific connection string for connecting to the database.
        /// </summary>

        public SqlCompactErrorLog(string connectionString)
        {
            if (connectionString == null)
                throw new ArgumentNullException("connectionString");

            if (connectionString.Length == 0)
                throw new ArgumentException(null, "connectionString");

            _connectionString = connectionString;

            InitializeDatabase();
        }

        private static readonly object _lock = new object();

        private void InitializeDatabase()
        {
            string connectionString = ConnectionString;
            Debug.AssertStringNotEmpty(connectionString);

            string dbFilePath = ConnectionStringHelper.GetDataSourceFilePath(connectionString);
            if (File.Exists(dbFilePath))
                return;

            //
            // Make sure that we don't have multiple threads all trying to create the database
            //

            lock (_lock)
            {

                //
                // Just double check that no other thread has created the database while
                // we were waiting for the lock
                //

                if (File.Exists(dbFilePath))
                    return;

                using (SqlCeEngine engine = new SqlCeEngine(ConnectionString))
                {
                    engine.CreateDatabase();
                }

                const string sql1 = @"
                CREATE TABLE ELMAH_Error (
                    [ErrorId] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT newid(),
                    [Application] NVARCHAR(60) NOT NULL,
                    [Host] NVARCHAR(50) NOT NULL,
                    [Type] NVARCHAR(100) NOT NULL,
                    [Source] NVARCHAR(60) NOT NULL,
                    [Message] NVARCHAR(500) NOT NULL,
                    [User] NVARCHAR(50) NOT NULL,
                    [StatusCode] INT NOT NULL,
                    [TimeUtc] DATETIME NOT NULL,
                    [Sequence] INT IDENTITY (1, 1) NOT NULL,
                    [AllXml] NTEXT NOT NULL
                )";

                const string sql2 = @"
                CREATE NONCLUSTERED INDEX [IX_Error_App_Time_Seq] ON [ELMAH_Error] 
                (
                    [Application]   ASC,
                    [TimeUtc]       DESC,
                    [Sequence]      DESC
                )"; 

                using (SqlCeConnection conn = new SqlCeConnection(ConnectionString))
                {
                    using (SqlCeCommand cmd = new SqlCeCommand())
                    {
                        conn.Open();
                        cmd.Connection = conn;
                        
                        cmd.CommandText = sql1;
                        cmd.ExecuteNonQuery();

                        cmd.CommandText = sql2;
                        cmd.ExecuteNonQuery();

                    }
                }
            }
        }


        /// <summary>
        /// Gets the name of this error log implementation.
        /// </summary>
        
        public override string Name
        {
            get { return "SQL Server Compact 4 Error Log"; }
        }

        /// <summary>
        /// Gets the connection string used by the log to connect to the database.
        /// </summary>
        public virtual string ConnectionString
        {
            get { return _connectionString; }
        }

        /// <summary>
        /// Logs an error to the database.
        /// </summary>
        /// <remarks>
        /// Use the stored procedure called by this implementation to set a
        /// policy on how long errors are kept in the log. The default
        /// implementation stores all errors for an indefinite time.
        /// </remarks>
        
        public override string Log(Error error)
        {
            if (error == null)
                throw new ArgumentNullException("error");

            string errorXml = ErrorXml.EncodeString(error);
            
            Guid id = Guid.NewGuid();
            
            const string query = @"
                INSERT INTO ELMAH_Error (
                    [ErrorId], [Application], [Host], 
                    [Type], [Source], [Message], [User], [StatusCode], 
                    [TimeUtc], [AllXml] )
                VALUES (
                    @ErrorId, @Application, @Host, 
                    @Type, @Source, @Message, @User, @StatusCode, 
                    @TimeUtc, @AllXml);";

            using (SqlCeConnection connection = new SqlCeConnection(ConnectionString))
            {
                using (SqlCeCommand command = new SqlCeCommand(query, connection))
                {
                    SqlCeParameterCollection parameters = command.Parameters;

                    parameters.Add("@ErrorId", SqlDbType.UniqueIdentifier).Value = id;
                    parameters.Add("@Application", SqlDbType.NVarChar, 60).Value = ApplicationName;
                    parameters.Add("@Host", SqlDbType.NVarChar, 30).Value = error.HostName;
                    parameters.Add("@Type", SqlDbType.NVarChar, 100).Value = error.Type;
                    parameters.Add("@Source", SqlDbType.NVarChar, 60).Value = error.Source;
                    parameters.Add("@Message", SqlDbType.NVarChar, 500).Value = error.Message;
                    parameters.Add("@User", SqlDbType.NVarChar, 50).Value = error.User;
                    parameters.Add("@StatusCode", SqlDbType.Int).Value = error.StatusCode;
                    parameters.Add("@TimeUtc", SqlDbType.DateTime).Value = error.Time.ToUniversalTime();
                    parameters.Add("@AllXml", SqlDbType.NText).Value = errorXml;

                    command.Connection = connection;
                    connection.Open();
                    command.ExecuteNonQuery();
                    return id.ToString();
                }
            }
        }

        /// <summary>
        /// Returns a page of errors from the databse in descending order 
        /// of logged time.
        /// </summary>
        /// 

        public override int GetErrors(int pageIndex, int pageSize, IList<ErrorLogEntry> errorEntryList)
        {
            if (pageIndex < 0)
                throw new ArgumentOutOfRangeException("pageIndex", pageIndex, null);

            if (pageSize < 0)
                throw new ArgumentOutOfRangeException("pageSize", pageSize, null);

            const string sql = @"
                SELECT
                    [ErrorId],
                    [Application],
                    [Host],
                    [Type],
                    [Source],
                    [Message],
                    [User],
                    [StatusCode],
                    [TimeUtc]
                FROM
                    [ELMAH_Error]
                WHERE
                    [Application] = @Application
                ORDER BY
                    [TimeUtc] DESC, 
                    [Sequence] DESC
                OFFSET @PageSize * @PageIndex ROWS FETCH NEXT @PageSize ROWS ONLY;
                ";

                const string getCount = @"
                SELECT COUNT(*) FROM [ELMAH_Error]";

            using (SqlCeConnection connection = new SqlCeConnection(ConnectionString))
            {
                connection.Open();

                using (SqlCeCommand command = new SqlCeCommand(sql, connection))
                {
                    SqlCeParameterCollection parameters = command.Parameters;

                    parameters.Add("@PageIndex", SqlDbType.Int).Value = pageIndex;
                    parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
                    parameters.Add("@Application", SqlDbType.NVarChar, 60).Value = ApplicationName;


                    using (SqlCeDataReader reader = command.ExecuteReader())
                    {
                        if (errorEntryList != null)
                        {
                            while (reader.Read())
                            {
                                string id = reader["ErrorId"].ToString();

                                Elmah.Error error = new Elmah.Error();
                                error.ApplicationName = reader["Application"].ToString();
                                error.HostName = reader["Host"].ToString();
                                error.Type = reader["Type"].ToString();
                                error.Source = reader["Source"].ToString();
                                error.Message = reader["Message"].ToString();
                                error.User = reader["User"].ToString();
                                error.StatusCode = Convert.ToInt32(reader["StatusCode"]);
                                error.Time = Convert.ToDateTime(reader["TimeUtc"]).ToLocalTime();
                                errorEntryList.Add(new ErrorLogEntry(this, id, error));
                            }
                        }
                    }
                }

                using (SqlCeCommand command = new SqlCeCommand(getCount, connection))
                {
                    return (int)command.ExecuteScalar();
                }
            }
        }

        /// <summary>
        /// Returns the specified error from the database, or null 
        /// if it does not exist.
        /// </summary>

        public override ErrorLogEntry GetError(string id)
        {
            if (id == null)
                throw new ArgumentNullException("id");

            if (id.Length == 0)
                throw new ArgumentException(null, "id");

            Guid errorGuid;

            try
            {
                errorGuid = new Guid(id);
            }
            catch (FormatException e)
            {
                throw new ArgumentException(e.Message, "id", e);
            }

            const string sql = @"
                SELECT 
                    [AllXml]
                FROM 
                    [ELMAH_Error]
                WHERE
                    [ErrorId] = @ErrorId";

            using (SqlCeConnection connection = new SqlCeConnection(ConnectionString))
            {
                using (SqlCeCommand command = new SqlCeCommand(sql, connection))
                {
                    command.Parameters.Add("@ErrorId", SqlDbType.UniqueIdentifier).Value = errorGuid;

                    connection.Open();

                    string errorXml = (string)command.ExecuteScalar();

                    if (errorXml == null)
                        return null;

                    Error error = ErrorXml.DecodeString(errorXml);
                    return new ErrorLogEntry(this, id, error);
                }
            }
        }
    }
}

#endif // !NET_1_1 && !NET_1_0