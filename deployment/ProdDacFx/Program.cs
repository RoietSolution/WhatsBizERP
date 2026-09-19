using System.Data.Common;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;

internal static class Program
{
    private const string ExpectedDatabase = "WhatsBizERP_PROD";
    private const string ExpectedServer = "sql.khatadhari.com,14330";

    private static int Main(string[] args)
    {
        try
        {
            var parsed = ParseArguments(args);
            if (parsed.Operation == "runtime-tls-probe")
                return RunRuntimeTlsProbe();

            if (parsed.Operation is "test-redaction" or "test-inspect-state-types" or "test-verify-backup" or "test-restore-backup" or "test-provision-runtime-user")
            {
                if (parsed.Operation == "test-redaction") RunRedactionSelfTest();
                else if (parsed.Operation == "test-inspect-state-types") RunInspectStateTypeSelfTest();
                else if (parsed.Operation == "test-verify-backup") RunVerifyBackupSelfTest();
                else if (parsed.Operation == "test-restore-backup") RunRestoreBackupSelfTest();
                else RunProvisionRuntimeUserSelfTest();
                return 0;
            }
            var connectionString = Environment.GetEnvironmentVariable("WHATSBIZ_PROD_SQL_CONNECTION");
            if (parsed.Operation == "provision-runtime-user")
            {
                try { ValidateConnection(connectionString, parsed.Database); }
                catch (Exception error) { throw new ProvisionRuntimeUserException("TARGET_CONNECTION", error); }
                return ProvisionRuntimeUser(connectionString!);
            }
            ValidateConnection(connectionString, parsed.Database);

            if (parsed.Operation == "validate")
            {
                Console.WriteLine("Production target and strict TLS requirements validated; no SQL connection was opened.");
                return 0;
            }

            if (parsed.Operation == "inspect-state")
            {
                InspectReadOnlyState(connectionString!);
                return 0;
            }

            if (parsed.Operation == "verify-backup")
                return VerifyBackup(connectionString!, parsed.BackupPath!);

            if (parsed.Operation == "restore-backup")
                return RestoreBackup(connectionString!, parsed.BackupPath!);

            using var package = DacPackage.Load(parsed.Dacpac);
            var options = new DacDeployOptions
            {
                BlockOnPossibleDataLoss = true,
                DropObjectsNotInSource = false,
                IgnoreColumnOrder = true,
                CreateNewDatabase = parsed.CreateDatabase
            };
            options.SetVariable("FreshProductionInitialization", parsed.FreshProduction.ToString());
            options.SetVariable("ProductionDeployment", "True");
            var services = new DacServices(connectionString!);
            services.Message += (_, eventArgs) =>
            {
                if (eventArgs.Message.MessageType == DacMessageType.Error)
                    Console.Error.WriteLine($"DacFx error: {Sanitize(eventArgs.Message.Message, connectionString)}");
            };

            switch (parsed.Operation)
            {
                case "report":
                    File.WriteAllText(parsed.Output!, services.GenerateDeployReport(package, parsed.Database, options));
                    break;
                case "script":
                    File.WriteAllText(parsed.Output!, services.GenerateDeployScript(package, parsed.Database, options));
                    break;
                case "publish":
                    services.Deploy(package, parsed.Database, true, options);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported DacFx operation.");
            }

            Console.WriteLine($"DacFx {parsed.Operation} completed for the verified production target.");
            return 0;
        }
        catch (Exception exception)
        {
            if (IsRuntimeTlsProbe(args))
            {
                WriteRuntimeTlsResult(false);
                return 1;
            }
            if (IsBackupOperation(args))
            {
                // Keep this allowlisted diagnostic silent about connection, SQL, and path details.
                Console.WriteLine("RESTORE BACKUP: FAIL");
                return 1;
            }
            if (IsProvisionRuntimeUserOperation(args))
            {
                WriteProvisionRuntimeUserFailure(exception, Environment.GetEnvironmentVariable("WHATSBIZ_PROD_SQL_CONNECTION"));
                return 1;
            }
            WriteSafeException(exception, Environment.GetEnvironmentVariable("WHATSBIZ_PROD_SQL_CONNECTION"));
            return 1;
        }
    }

    private static bool IsProvisionRuntimeUserOperation(string[] args)
    {
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals("--operation", StringComparison.OrdinalIgnoreCase))
                return args[i + 1].Equals("provision-runtime-user", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static int ProvisionRuntimeUser(string connectionString)
    {
        var targetBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = ExpectedDatabase };
        using var target = new SqlConnection(targetBuilder.ConnectionString);
        try { target.Open(); }
        catch (Exception error) { throw new ProvisionRuntimeUserException("DATABASE_ACCESS", error); }
        SqlTransaction transaction;
        try { transaction = target.BeginTransaction(); }
        catch (Exception error) { throw new ProvisionRuntimeUserException("DATABASE_ACCESS", error); }
        using (transaction)
        {
        try
        {
            int userId;
            try
            {
                using var inspect = new SqlCommand("SELECT USER_ID(N'whatsbiz_prod');", target, transaction);
                var value = inspect.ExecuteScalar();
                userId = value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception error) { throw new ProvisionRuntimeUserException("USER_INSPECTION", error); }

            if (userId == 0)
            {
                try
                {
                    using var create = new SqlCommand("CREATE USER [whatsbiz_prod] FOR LOGIN [whatsbiz_prod];", target, transaction);
                    create.ExecuteNonQuery();
                }
                catch (Exception error) { throw new ProvisionRuntimeUserException("CREATE_USER", error); }
            }
            else
            {
                try
                {
                    using var remap = new SqlCommand("ALTER USER [whatsbiz_prod] WITH LOGIN = [whatsbiz_prod];", target, transaction);
                    remap.ExecuteNonQuery();
                }
                catch (Exception error) { throw new ProvisionRuntimeUserException("ALTER_USER_MAPPING", error); }
            }

            try
            {
                using var connect = new SqlCommand(BuildProvisionRuntimeUserConnectSql(), target, transaction);
                connect.ExecuteNonQuery();
            }
            catch (Exception error) { throw new ProvisionRuntimeUserException("GRANT_CONNECT", error); }

            try
            {
                using var verify = new SqlCommand(BuildRuntimeUserVerificationSql(), target, transaction);
                verify.ExecuteNonQuery();
            }
            catch (Exception error) { throw new ProvisionRuntimeUserException("FINAL_VERIFICATION", error); }
            transaction.Commit();
            Console.WriteLine("PROVISION RUNTIME USER: PASS");
            Console.WriteLine("Login: whatsbiz_prod");
            Console.WriteLine("Database: WhatsBizERP_PROD");
            Console.WriteLine("Database user mapped: YES");
            Console.WriteLine("HAS_DBACCESS: 1");
            Console.WriteLine("CONNECT: YES");
            Console.WriteLine("db_datareader: YES");
            Console.WriteLine("db_datawriter: YES");
            Console.WriteLine("EXECUTE: YES");
            Console.WriteLine("sysadmin: NO where visible");
            Console.WriteLine("dbcreator: NO where visible");
            Console.WriteLine("db_owner: NO");
            return 0;
        }
        catch (ProvisionRuntimeUserException)
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
        catch (Exception error)
        {
            try { transaction.Rollback(); } catch { }
            throw new ProvisionRuntimeUserException("FINAL_VERIFICATION", error);
        }
        }
    }

    private static string BuildProvisionRuntimeUserConnectSql() => """
        IF EXISTS
        (
            SELECT 1 FROM sys.database_permissions
            WHERE grantee_principal_id = USER_ID(N'whatsbiz_prod')
              AND class = 0 AND permission_name = N'CONNECT' AND state = 'D'
        )
            REVOKE CONNECT FROM [whatsbiz_prod];
        GRANT CONNECT TO [whatsbiz_prod];
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.database_role_members AS drm
            JOIN sys.database_principals AS role ON role.principal_id = drm.role_principal_id
            JOIN sys.database_principals AS member ON member.principal_id = drm.member_principal_id
            WHERE role.name = N'db_datareader' AND member.name = N'whatsbiz_prod'
        )
            ALTER ROLE [db_datareader] ADD MEMBER [whatsbiz_prod];
        IF NOT EXISTS
        (
            SELECT 1
            FROM sys.database_role_members AS drm
            JOIN sys.database_principals AS role ON role.principal_id = drm.role_principal_id
            JOIN sys.database_principals AS member ON member.principal_id = drm.member_principal_id
            WHERE role.name = N'db_datawriter' AND member.name = N'whatsbiz_prod'
        )
            ALTER ROLE [db_datawriter] ADD MEMBER [whatsbiz_prod];
        GRANT EXECUTE TO [whatsbiz_prod];
        """;

    private static string BuildRuntimeUserVerificationSql() => """
        DECLARE @principalId int = USER_ID(N'whatsbiz_prod');
        DECLARE @authenticationType nvarchar(60) =
            (SELECT authentication_type_desc FROM sys.database_principals WHERE principal_id = @principalId);
        DECLARE @userSid varbinary(85) =
            (SELECT sid FROM sys.database_principals WHERE principal_id = @principalId);
        DECLARE @loginSid varbinary(85) = SUSER_SID(N'whatsbiz_prod');
        DECLARE @connectGranted bit = CASE WHEN EXISTS
        (
            SELECT 1 FROM sys.database_permissions
            WHERE grantee_principal_id = @principalId AND class = 0
              AND permission_name = N'CONNECT' AND state IN ('G', 'W')
        ) THEN 1 ELSE 0 END;
        DECLARE @readerMember bit = CASE WHEN IS_ROLEMEMBER(N'db_datareader', N'whatsbiz_prod') = 1 THEN 1 ELSE 0 END;
        DECLARE @writerMember bit = CASE WHEN IS_ROLEMEMBER(N'db_datawriter', N'whatsbiz_prod') = 1 THEN 1 ELSE 0 END;
        DECLARE @executeGranted bit = CASE WHEN EXISTS
        (
            SELECT 1 FROM sys.database_permissions
            WHERE grantee_principal_id = @principalId AND class = 0
              AND permission_name = N'EXECUTE' AND state IN ('G', 'W')
        ) THEN 1 ELSE 0 END;
        DECLARE @ownerMember bit = CASE WHEN IS_ROLEMEMBER(N'db_owner', N'whatsbiz_prod') = 1 THEN 1 ELSE 0 END;
        DECLARE @hasAccess int;
        EXECUTE AS USER = N'whatsbiz_prod';
        SELECT @hasAccess = HAS_DBACCESS(N'WhatsBizERP_PROD');
        REVERT;
        IF DB_NAME() <> N'WhatsBizERP_PROD' OR @principalId IS NULL
           OR @authenticationType <> N'INSTANCE' OR @connectGranted <> 1
           OR (@loginSid IS NOT NULL AND @userSid <> @loginSid)
           OR (@hasAccess IS NOT NULL AND @hasAccess <> 1)
           OR @readerMember <> 1 OR @writerMember <> 1 OR @executeGranted <> 1 OR @ownerMember <> 0
            THROW 51940, 'Runtime database user verification failed.', 1;
        """;

    private static void RunProvisionRuntimeUserSelfTest()
    {
        var valid = ParseArguments(new[] { "--operation", "provision-runtime-user", "--database", ExpectedDatabase });
        if (valid.Operation != "provision-runtime-user" || valid.Database != ExpectedDatabase)
            throw new InvalidOperationException("Valid runtime-user provisioning arguments were not parsed safely.");
        foreach (var database in new[] { "WhatsBizERP_QA", "WhatsBizERP", "OtherDb", "" })
            AssertRejected(() => ParseArguments(new[] { "--operation", "provision-runtime-user", "--database", database }), $"runtime-user target {database}");
        AssertRejected(() => ParseArguments(new[] { "--operation", "provision-runtime-user", "--database", ExpectedDatabase, "--login", "other_login" }), "runtime login override");

        const string strictConnection = "Server=sql.khatadhari.com,14330;Database=WhatsBizERP_PROD;User ID=dummy-user;Password=dummy-secret;Encrypt=True;TrustServerCertificate=False";
        ValidateConnection(strictConnection, ExpectedDatabase);
        AssertRejected(() => ValidateConnection(strictConnection.Replace("Encrypt=True", "Encrypt=False", StringComparison.Ordinal), ExpectedDatabase), "runtime-user Encrypt=False");
        AssertRejected(() => ValidateConnection(strictConnection.Replace("TrustServerCertificate=False", "TrustServerCertificate=True", StringComparison.Ordinal), ExpectedDatabase), "runtime-user TrustServerCertificate=True");

        const string createSql = "CREATE USER [whatsbiz_prod] FOR LOGIN [whatsbiz_prod];";
        const string alterSql = "ALTER USER [whatsbiz_prod] WITH LOGIN = [whatsbiz_prod];";
        var connectSql = BuildProvisionRuntimeUserConnectSql();
        var verificationSql = BuildRuntimeUserVerificationSql();
        if (!createSql.Contains("FOR LOGIN [whatsbiz_prod]", StringComparison.Ordinal) ||
            !alterSql.Contains("WITH LOGIN = [whatsbiz_prod]", StringComparison.Ordinal) ||
            !connectSql.Contains("GRANT CONNECT TO [whatsbiz_prod]", StringComparison.Ordinal) ||
            !verificationSql.Contains("USER_ID(N'whatsbiz_prod')", StringComparison.Ordinal) ||
            !verificationSql.Contains("authentication_type_desc", StringComparison.Ordinal) ||
            !verificationSql.Contains("N'INSTANCE'", StringComparison.Ordinal) ||
            !verificationSql.Contains("SUSER_SID(N'whatsbiz_prod')", StringComparison.Ordinal) ||
            !verificationSql.Contains("HAS_DBACCESS(N'WhatsBizERP_PROD')", StringComparison.Ordinal) ||
            !connectSql.Contains("ALTER ROLE [db_datareader] ADD MEMBER [whatsbiz_prod]", StringComparison.Ordinal) ||
            !connectSql.Contains("ALTER ROLE [db_datawriter] ADD MEMBER [whatsbiz_prod]", StringComparison.Ordinal) ||
            !connectSql.Contains("GRANT EXECUTE TO [whatsbiz_prod]", StringComparison.Ordinal) ||
            !verificationSql.Contains("db_datareader", StringComparison.Ordinal) ||
            !verificationSql.Contains("db_datawriter", StringComparison.Ordinal) ||
            !verificationSql.Contains("permission_name = N'EXECUTE'", StringComparison.Ordinal) ||
            verificationSql.Contains("sys.server_principals", StringComparison.OrdinalIgnoreCase) ||
            verificationSql.Contains("IS_SRVROLEMEMBER", StringComparison.OrdinalIgnoreCase) ||
            connectSql.Contains("db_owner", StringComparison.OrdinalIgnoreCase) ||
            connectSql.Contains("sysadmin", StringComparison.OrdinalIgnoreCase) ||
            connectSql.Contains("dbcreator", StringComparison.OrdinalIgnoreCase) || connectSql.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Runtime-user SQL exceeded the narrowly allowlisted principal/access operations.");

        const string password = "example-P@ssword";
        const string token = "example-access-token";
        const string conn = "Server=secret-host;Database=secret-db;User ID=secret-user;Password=example-P@ssword";
        using var capture = new StringWriter(CultureInfo.InvariantCulture);
        var original = Console.Out;
        try
        {
            Console.SetOut(capture);
            WriteProvisionRuntimeUserFailure(
                new ProvisionRuntimeUserException("USER_INSPECTION",
                    new InvalidOperationException($"Metadata denied: {conn}; access_token={token}; detail=example-P@ssword")),
                "Server=sql.khatadhari.com,14330;Database=WhatsBizERP_PROD;User ID=deployment-user;Password=example-P@ssword;Encrypt=True;TrustServerCertificate=False");
        }
        finally { Console.SetOut(original); }
        var output = capture.ToString();
        var failures = new List<string>();
        if (!output.Contains("Stage: USER_INSPECTION", StringComparison.Ordinal)) failures.Add("stage");
        if (!output.Contains("Database: WhatsBizERP_PROD", StringComparison.Ordinal)) failures.Add("database label");
        if (!output.Contains("Login: whatsbiz_prod", StringComparison.Ordinal)) failures.Add("login label");
        if (!output.Contains("Reason: Metadata denied", StringComparison.Ordinal)) failures.Add("reason retained");
        if (!output.Contains("SQL Error Number: N/A", StringComparison.Ordinal)) failures.Add("SQL number fallback");
        if (output.Contains(password, StringComparison.Ordinal) || output.Contains(token, StringComparison.Ordinal) ||
            output.Contains("secret-host", StringComparison.Ordinal) || output.Contains("secret-db", StringComparison.Ordinal) ||
            output.Contains("secret-user", StringComparison.Ordinal) || output.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Password", StringComparison.OrdinalIgnoreCase) || output.Contains("access_token", StringComparison.OrdinalIgnoreCase)) failures.Add("secret redaction");
        if (failures.Count > 0) throw new InvalidOperationException($"Runtime-user diagnostic self-test failed: {string.Join(", ", failures)}.");

        Console.WriteLine("PASS: runtime-user stage/error formatting and connection/secret redaction.");
    }

    private static void WriteProvisionRuntimeUserFailure(Exception exception, string? connectionString)
    {
        var stageError = FindException<ProvisionRuntimeUserException>(exception);
        var stage = stageError?.Stage is "TARGET_CONNECTION" or "DATABASE_ACCESS" or
            "USER_INSPECTION" or "CREATE_USER" or "ALTER_USER_MAPPING" or "GRANT_CONNECT" or "FINAL_VERIFICATION"
            ? stageError.Stage
            : "TARGET_CONNECTION";
        var sqlError = FindException<SqlException>(exception);
        var firstSqlError = sqlError?.Errors.Count > 0 ? sqlError.Errors[0] : null;
        var reasonSource = firstSqlError?.Message ?? FindInnermost(exception).Message;
        var reason = SanitizeProvisionReason(reasonSource, connectionString);

        Console.WriteLine("PROVISION RUNTIME USER: FAIL");
        Console.WriteLine($"Stage: {stage}");
        Console.WriteLine("Database: WhatsBizERP_PROD");
        Console.WriteLine("Login: whatsbiz_prod");
        Console.WriteLine($"SQL Error Number: {(firstSqlError is null ? "N/A" : firstSqlError.Number.ToString(CultureInfo.InvariantCulture))}");
        Console.WriteLine($"SQL Error State: {(firstSqlError is null ? "N/A" : firstSqlError.State.ToString(CultureInfo.InvariantCulture))}");
        Console.WriteLine($"Reason: {reason}");
    }

    private static TException? FindException<TException>(Exception exception) where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException!)
            if (current is TException found) return found;
        return null;
    }

    private static Exception FindInnermost(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null) current = current.InnerException;
        return current;
    }

    private static string SanitizeProvisionReason(string? value, string? connectionString)
    {
        var safe = value ?? "(empty)";
        safe = Regex.Replace(safe,
            @"(?i)\b(server|data\s+source|address|addr|network\s+address|database|initial\s+catalog|user\s+id|uid|password|pwd|access[_ -]?token|refresh[_ -]?token|app[_ -]?secret|client[_ -]?secret|verify[_ -]?token|authorization[_ -]?code|auth[_ -]?code|pin)\s*=\s*(?:'[^']*'|""[^""]*""|[^\s,;]+)",
            "[REDACTED]");
        return Sanitize(safe, connectionString);
    }

    private sealed class ProvisionRuntimeUserException(string stage, Exception innerException)
        : Exception("Runtime-user provisioning phase failed.", innerException)
    {
        public string Stage { get; } = stage;
    }

    private static bool IsRuntimeTlsProbe(string[] args) =>
        args.Length >= 2 && args[0].Equals("--operation", StringComparison.OrdinalIgnoreCase) &&
        args[1].Equals("runtime-tls-probe", StringComparison.OrdinalIgnoreCase);

    private static int RunRuntimeTlsProbe()
    {
        string? password = null;
        try
        {
            var userName = Console.In.ReadLine();
            password = Console.In.ReadLine();
            if (!string.Equals(userName, "whatsbiz_prod", StringComparison.Ordinal) || string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Runtime credential unavailable or invalid.");

            // The manual PowerShell probe uses the existing SSH tunnel's local listener.
            // The certificate identity remains the SQL hostname, not the tunnel endpoint.
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = "127.0.0.1,14330",
                InitialCatalog = ExpectedDatabase,
                UserID = userName,
                Password = password,
                Encrypt = true,
                TrustServerCertificate = false,
                HostNameInCertificate = "sql.khatadhari.com",
                ConnectTimeout = 8
            };

            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = new SqlCommand("SELECT DB_NAME();", connection);
            var database = Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (!string.Equals(database, ExpectedDatabase, StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected database target.");

            WriteRuntimeTlsResult(true);
            return 0;
        }
        catch
        {
            WriteRuntimeTlsResult(false);
            return 1;
        }
        finally
        {
            password = null;
        }
    }

    private static void WriteRuntimeTlsResult(bool passed)
    {
        Console.WriteLine($"SQLCLIENT TLS: {(passed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Database: {ExpectedDatabase}");
        Console.WriteLine("Encrypt: True");
        Console.WriteLine("TrustServerCertificate: False");
        Console.WriteLine("HostNameInCertificate: sql.khatadhari.com");
    }

    private static bool IsBackupOperation(string[] args)
    {
        for (var i = 0; i + 1 < args.Length; i++)
            if (args[i].Equals("--operation", StringComparison.OrdinalIgnoreCase))
                return args[i + 1] is var operation &&
                    (operation.Equals("verify-backup", StringComparison.OrdinalIgnoreCase) || operation.Equals("restore-backup", StringComparison.OrdinalIgnoreCase));
        return false;
    }

    private static int VerifyBackup(string connectionString, string backupPath)
    {
        var escapedPath = backupPath.Replace("'", "''", StringComparison.Ordinal);
        var masterConnection = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        using var connection = new SqlConnection(masterConnection.ConnectionString);
        try
        {
            connection.Open();
            using (var verify = new SqlCommand($"RESTORE VERIFYONLY FROM DISK = N'{escapedPath}' WITH CHECKSUM;", connection)
                   { CommandTimeout = 120 })
            {
                verify.ExecuteNonQuery();
            }
            Console.WriteLine("RESTORE VERIFYONLY: PASS");
        }
        catch
        {
            WriteVerifyFailure();
            return 1;
        }

        try
        {
            using var header = new SqlCommand($"RESTORE HEADERONLY FROM DISK = N'{escapedPath}';", connection)
            { CommandTimeout = 120 };
            using var reader = header.ExecuteReader();
            if (!reader.Read())
            {
                Console.WriteLine("RESTORE HEADERONLY: FAIL");
                return 1;
            }

            var databaseName = ReadHeaderValue(reader, "DatabaseName");
            if (!string.Equals(databaseName, ExpectedDatabase, StringComparison.Ordinal))
            {
                Console.WriteLine("RESTORE HEADERONLY: FAIL");
                return 1;
            }

            var backupType = ReadBackupTypeDescription(reader);
            var start = FormatHeaderValue(ReadHeaderObject(reader, "BackupStartDate"));
            var finish = FormatHeaderValue(ReadHeaderObject(reader, "BackupFinishDate"));
            Console.WriteLine($"DatabaseName: {databaseName}");
            Console.WriteLine($"BackupTypeDescription: {backupType}");
            Console.WriteLine($"BackupStartDate: {start}");
            Console.WriteLine($"BackupFinishDate: {finish}");
            return 0;
        }
        catch
        {
            Console.WriteLine("RESTORE HEADERONLY: FAIL");
            return 1;
        }
    }

    private static int RestoreBackup(string connectionString, string backupPath)
    {
        var singleUserAttempted = false;
        try
        {
            var escapedBackupPath = QuoteSqlLiteral(ValidateBackupPath(backupPath));
            var masterBuilder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
            using var connection = new SqlConnection(masterBuilder.ConnectionString);
            connection.Open();

            using (var verify = new SqlCommand($"RESTORE VERIFYONLY FROM DISK = N'{escapedBackupPath}' WITH CHECKSUM;", connection) { CommandTimeout = 120 })
                verify.ExecuteNonQuery();

            using (var headerCommand = new SqlCommand($"RESTORE HEADERONLY FROM DISK = N'{escapedBackupPath}';", connection) { CommandTimeout = 120 })
            using (var header = headerCommand.ExecuteReader())
            {
                if (!header.Read() || !string.Equals(ReadHeaderValue(header, "DatabaseName"), ExpectedDatabase, StringComparison.Ordinal) ||
                    Convert.ToInt32(ReadHeaderObject(header, "BackupType"), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException("Backup header is not the required production database backup.");
            }

            var backupFiles = ReadBackupFiles(connection, escapedBackupPath);
            var targetFiles = ReadExistingProductionFiles(connection);
            var restoreSql = BuildRestoreCommand(backupPath, backupFiles, targetFiles);

            // A restore needs exclusive access. Always attempt to return the database to MULTI_USER,
            // including when SINGLE_USER or RESTORE reports an error.
            try
            {
                singleUserAttempted = true;
                ExecuteFixedSql(connection, "ALTER DATABASE [WhatsBizERP_PROD] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;");
                ExecuteFixedSql(connection, restoreSql);
            }
            finally
            {
                if (singleUserAttempted)
                    ExecuteFixedSql(connection, "ALTER DATABASE [WhatsBizERP_PROD] SET MULTI_USER;");
            }

            Console.WriteLine("RESTORE BACKUP: PASS");
            return 0;
        }
        catch
        {
            Console.WriteLine("RESTORE BACKUP: FAIL");
            return 1;
        }
    }

    private static List<BackupFile> ReadBackupFiles(SqlConnection connection, string escapedBackupPath)
    {
        var files = new List<BackupFile>();
        using var command = new SqlCommand($"RESTORE FILELISTONLY FROM DISK = N'{escapedBackupPath}';", connection) { CommandTimeout = 120 };
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var logicalName = Convert.ToString(reader["LogicalName"], CultureInfo.InvariantCulture) ?? string.Empty;
            var type = Convert.ToString(reader["Type"], CultureInfo.InvariantCulture) ?? string.Empty;
            var fileId = Convert.ToInt32(reader["FileId"], CultureInfo.InvariantCulture);
            var isPresent = Convert.ToBoolean(reader["IsPresent"], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(logicalName) || type is not ("D" or "L") || !isPresent)
                throw new InvalidOperationException("Backup contains an unsupported or absent file entry.");
            files.Add(new BackupFile(logicalName, type[0], fileId));
        }
        if (files.Count == 0) throw new InvalidOperationException("Backup contains no data/log files.");
        return files;
    }

    private static List<TargetFile> ReadExistingProductionFiles(SqlConnection connection)
    {
        const string sql = @"
SELECT file_id, type, physical_name
FROM sys.master_files
WHERE database_id = DB_ID(N'WhatsBizERP_PROD')
ORDER BY type, file_id;";
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        using var reader = command.ExecuteReader();
        var files = new List<TargetFile>();
        while (reader.Read())
        {
            var typeCode = reader.GetByte(1);
            var type = typeCode switch { 0 => 'D', 1 => 'L', _ => '?' };
            var physicalName = reader.GetString(2);
            if (type == '?' || string.IsNullOrWhiteSpace(physicalName))
                throw new InvalidOperationException("Production database has unsupported file metadata.");
            files.Add(new TargetFile(reader.GetInt32(0), type, physicalName));
        }
        if (files.Count == 0) throw new InvalidOperationException("Existing production database files were not found.");
        return files;
    }

    private static string BuildRestoreCommand(string backupPath, IReadOnlyCollection<BackupFile> backupFiles, IReadOnlyCollection<TargetFile> targetFiles)
    {
        _ = ValidateBackupPath(backupPath);
        var moves = new List<string>();
        foreach (var type in new[] { 'D', 'L' })
        {
            var source = backupFiles.Where(file => file.Type == type).OrderBy(file => file.FileId).ToArray();
            var target = targetFiles.Where(file => file.Type == type).OrderBy(file => file.FileId).ToArray();
            if (source.Length != target.Length || source.Length == 0)
                throw new InvalidOperationException("Backup and existing production file layouts do not match safely.");
            for (var index = 0; index < source.Length; index++)
                moves.Add($"MOVE N'{QuoteSqlLiteral(source[index].LogicalName)}' TO N'{QuoteSqlLiteral(target[index].PhysicalName)}'");
        }

        if (backupFiles.Select(file => file.LogicalName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != backupFiles.Count ||
            targetFiles.Select(file => file.PhysicalName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targetFiles.Count)
            throw new InvalidOperationException("Backup or target file metadata is ambiguous.");

        var path = QuoteSqlLiteral(backupPath);
        return $"RESTORE DATABASE [WhatsBizERP_PROD] FROM DISK = N'{path}' WITH REPLACE, RECOVERY, {string.Join(", ", moves)};";
    }

    private static string QuoteSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static void ExecuteFixedSql(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
        command.ExecuteNonQuery();
    }

    private sealed record BackupFile(string LogicalName, char Type, int FileId);
    private sealed record TargetFile(int FileId, char Type, string PhysicalName);

    private static void WriteVerifyFailure() => Console.WriteLine("RESTORE VERIFYONLY: FAIL");

    private static object? ReadHeaderObject(DbDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal);
    }

    private static string ReadHeaderValue(DbDataReader reader, string column) =>
        Convert.ToString(ReadHeaderObject(reader, column), CultureInfo.InvariantCulture) ?? string.Empty;

    private static string ReadBackupTypeDescription(DbDataReader reader)
    {
        var descriptionOrdinal = -1;
        for (var i = 0; i < reader.FieldCount; i++)
            if (reader.GetName(i).Equals("BackupTypeDescription", StringComparison.OrdinalIgnoreCase))
                descriptionOrdinal = i;
        if (descriptionOrdinal >= 0 && !reader.IsDBNull(descriptionOrdinal))
            return Convert.ToString(reader.GetValue(descriptionOrdinal), CultureInfo.InvariantCulture) ?? string.Empty;

        var type = Convert.ToInt32(ReadHeaderObject(reader, "BackupType"), CultureInfo.InvariantCulture);
        return type switch
        {
            1 => "Database",
            2 => "Transaction Log",
            4 => "File or Filegroup",
            5 => "Differential Database",
            6 => "Differential File or Filegroup",
            7 => "Differential Transaction Log",
            8 => "Partial",
            9 => "Differential Partial",
            _ => $"Other (type {type})"
        };
    }

    private static string FormatHeaderValue(object? value) => value switch
    {
        null => string.Empty,
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static string ValidateBackupPath(string path)
    {
        const string allowedRoot = "/var/opt/mssql/backup/";
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith(allowedRoot, StringComparison.Ordinal) ||
            path.Contains('\\') || path.Contains('\0') || path.Any(char.IsControl) || path.Contains('\'') ||
            !path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid production backup path.");

        var segments = path.Split('/');
        if (segments.Any(segment => segment is "." or "..") || segments[^1].Length == 0)
            throw new ArgumentException("Invalid production backup path.");
        return path;
    }

    private static void RunVerifyBackupSelfTest()
    {
        const string validPath = "/var/opt/mssql/backup/WhatsBizERP_PROD-pre-test.bak";
        if (ValidateBackupPath(validPath) != validPath) throw new InvalidOperationException("Valid backup path was rejected.");
        var parsed = ParseArguments(new[] { "--operation", "verify-backup", "--database", ExpectedDatabase, "--backup-path", validPath });
        if (parsed.Operation != "verify-backup" || parsed.Database != ExpectedDatabase || parsed.BackupPath != validPath)
            throw new InvalidOperationException("Valid backup verification arguments were not parsed safely.");
        AssertRejected(() => ValidateBackupPath("/var/opt/mssql/backup/../outside.bak"), "path traversal");
        AssertRejected(() => ValidateBackupPath("/var/opt/mssql/backup/file.bakx"), "non-bak extension");
        AssertRejected(() => ValidateBackupPath("/tmp/file.bak"), "path outside approved backup directory");

        const string strictConnection = "Server=sql.khatadhari.com,14330;Database=WhatsBizERP_PROD;User ID=dummy-user;Password=dummy-secret;Encrypt=True;TrustServerCertificate=False";
        ValidateConnection(strictConnection, ExpectedDatabase);
        foreach (var database in new[] { "WhatsBizERP_QA", "WhatsBizERP", "OtherDb" })
        {
            AssertRejected(() => ValidateConnection(strictConnection.Replace("WhatsBizERP_PROD", database, StringComparison.Ordinal), database), $"database {database}");
            AssertRejected(() => ParseArguments(new[] { "--operation", "verify-backup", "--database", database, "--backup-path", validPath }), $"backup operation target {database}");
        }
        AssertRejected(() => ValidateConnection(strictConnection.Replace("Encrypt=True", "Encrypt=False", StringComparison.Ordinal), ExpectedDatabase), "Encrypt=False");
        AssertRejected(() => ValidateConnection(strictConnection.Replace("TrustServerCertificate=False", "TrustServerCertificate=True", StringComparison.Ordinal), ExpectedDatabase), "TrustServerCertificate=True");

        using var capture = new StringWriter(CultureInfo.InvariantCulture);
        var original = Console.Out;
        try
        {
            Console.SetOut(capture);
            WriteVerifyFailure();
        }
        finally { Console.SetOut(original); }
        var output = capture.ToString();
        if (output.Contains("dummy-secret", StringComparison.Ordinal) || output.Contains("dummy-user", StringComparison.Ordinal) || output.Contains("Server=", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backup verification output exposed sensitive connection information.");
        Console.WriteLine("PASS: backup path, production database, strict TLS, and output-redaction checks.");
    }

    private static void RunRestoreBackupSelfTest()
    {
        const string path = "/var/opt/mssql/backup/WhatsBizERP_PROD-pre-test.bak";
        var parsed = ParseArguments(new[] { "--operation", "restore-backup", "--database", ExpectedDatabase, "--backup-path", path });
        if (parsed.Operation != "restore-backup" || parsed.Database != ExpectedDatabase || parsed.BackupPath != path)
            throw new InvalidOperationException("Valid restore-backup arguments were not parsed safely.");

        foreach (var database in new[] { "WhatsBizERP_QA", "WhatsBizERP", "OtherDb" })
            AssertRejected(() => ParseArguments(new[] { "--operation", "restore-backup", "--database", database, "--backup-path", path }), $"restore target {database}");
        AssertRejected(() => ParseArguments(new[] { "--operation", "restore-backup", "--database", ExpectedDatabase, "--backup-path", "/var/opt/mssql/backup/../other.bak" }), "restore path traversal");
        AssertRejected(() => ParseArguments(new[] { "--operation", "restore-backup", "--database", ExpectedDatabase, "--backup-path", "/var/opt/mssql/backup/file.txt" }), "restore non-bak extension");
        AssertRejected(() => ParseArguments(new[] { "--operation", "restore-backup", "--database", ExpectedDatabase, "--backup-path", "/tmp/file.bak" }), "restore path outside backup directory");
        AssertRejected(() => ParseArguments(new[] { "--operation", "restore-backup", "--database", ExpectedDatabase, "--backup-path", path, "--sql", "DROP DATABASE" }), "arbitrary SQL argument");

        var strict = "Server=sql.khatadhari.com,14330;Database=WhatsBizERP_PROD;User ID=dummy-user;Password=dummy-secret;Encrypt=True;TrustServerCertificate=False";
        ValidateConnection(strict, ExpectedDatabase);
        AssertRejected(() => ValidateConnection(strict.Replace("Encrypt=True", "Encrypt=False", StringComparison.Ordinal), ExpectedDatabase), "restore Encrypt=False");
        AssertRejected(() => ValidateConnection(strict.Replace("TrustServerCertificate=False", "TrustServerCertificate=True", StringComparison.Ordinal), ExpectedDatabase), "restore TrustServerCertificate=True");

        var sql = BuildRestoreCommand(path,
            new[] { new BackupFile("ProdData", 'D', 1), new BackupFile("ProdLog", 'L', 2) },
            new[] { new TargetFile(1, 'D', "/var/opt/mssql/data/WhatsBizERP_PROD.mdf"), new TargetFile(2, 'L', "/var/opt/mssql/data/WhatsBizERP_PROD_log.ldf") });
        if (!sql.Contains("RESTORE DATABASE [WhatsBizERP_PROD]", StringComparison.Ordinal) ||
            !sql.Contains("MOVE N'ProdData' TO N'/var/opt/mssql/data/WhatsBizERP_PROD.mdf'", StringComparison.Ordinal) ||
            !sql.Contains("MOVE N'ProdLog' TO N'/var/opt/mssql/data/WhatsBizERP_PROD_log.ldf'", StringComparison.Ordinal) ||
            sql.Contains("WhatsBizERP_QA", StringComparison.Ordinal) || sql.Contains("DROP DATABASE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Restore SQL is not restricted to the intended PROD database/file mapping.");

        AssertRejected(() => BuildRestoreCommand(path,
            new[] { new BackupFile("ProdData", 'D', 1), new BackupFile("ProdLog", 'L', 2) },
            new[] { new TargetFile(1, 'D', "/var/opt/mssql/data/WhatsBizERP_PROD.mdf") }), "mismatched existing file layout");

        using var capture = new StringWriter(CultureInfo.InvariantCulture);
        var original = Console.Out;
        try { Console.SetOut(capture); Console.WriteLine("RESTORE BACKUP: FAIL"); }
        finally { Console.SetOut(original); }
        var output = capture.ToString();
        if (output.Contains("dummy-secret", StringComparison.Ordinal) || output.Contains("dummy-user", StringComparison.Ordinal) || output.Contains("Server=", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Restore failure output exposed connection credentials.");
        Console.WriteLine("PASS: restore-backup target, path, TLS, file mapping, SQL allowlist, and redaction guards.");
    }

    private static void AssertRejected(Action action, string caseName)
    {
        try { action(); }
        catch { return; }
        throw new InvalidOperationException($"Expected rejection for {caseName}.");
    }

    private static void WriteSafeException(Exception exception, string? connectionString)
    {
        Console.WriteLine($"DacFx operation failed ({exception.GetType().FullName}).");
        Console.WriteLine($"  Message: {Sanitize(exception.Message, connectionString)}");

        if (exception is DacServicesException dacException)
        {
            foreach (var message in dacException.Messages.Cast<object>().Take(40))
            {
                var messageText = ReadStringProperty(message, "Message");
                var number = ReadStringProperty(message, "Number");
                var prefix = ReadStringProperty(message, "Prefix");
                var type = ReadStringProperty(message, "MessageType") ?? ReadStringProperty(message, "Type");
                var identity = string.Join(" ", new[] { prefix, number, type }.Where(value => !string.IsNullOrWhiteSpace(value)));
                Console.WriteLine($"  DacFx {(identity.Length == 0 ? "message" : identity)}: {Sanitize(messageText ?? "(message text unavailable)", connectionString)}");
            }
        }

        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (!ReferenceEquals(inner, exception))
                Console.WriteLine($"  Inner ({inner.GetType().FullName}): {Sanitize(inner.Message, connectionString)}");
            if (inner is SqlException sqlException)
            {
                foreach (SqlError error in sqlException.Errors)
                    Console.WriteLine($"  SQL error number={error.Number}; state={error.State}; class={error.Class}; procedure={Sanitize(error.Procedure, connectionString)}; line={error.LineNumber}; message={Sanitize(error.Message, connectionString)}");
            }
        }
    }

    private static string? ReadStringProperty(object instance, string name)
    {
        try { return Convert.ToString(instance.GetType().GetProperty(name)?.GetValue(instance), CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static string Sanitize(string? value, string? connectionString)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        var safe = value;
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                if (!string.IsNullOrEmpty(builder.Password)) safe = safe.Replace(builder.Password, "[REDACTED]", StringComparison.Ordinal);
            }
            catch { /* Do not echo malformed connection input. Generic redaction below still applies. */ }
        }

        // If a full connection string was embedded in a message, redact that whole value.
        safe = Regex.Replace(safe,
            @"(?im)^.*(?=.*\b(?:data\s+source|server)\s*=)(?=.*\b(?:password|pwd)\s*=).*$",
            "[REDACTED_CONNECTION_STRING]");
        safe = Regex.Replace(safe,
            @"(?i)\b(password|pwd|access[_ -]?token|refresh[_ -]?token|app[_ -]?secret|client[_ -]?secret|verify[_ -]?token|authorization[_ -]?code|auth[_ -]?code|pin)\s*=\s*(?:'[^']*'|""[^""]*""|[^\s,;]+)",
            "$1=[REDACTED]");
        safe = Regex.Replace(safe, @"(?i)(bearer\s+)[A-Za-z0-9._~+/-]+=*", "$1[REDACTED]");
        return safe.Length > 1200 ? safe[..1200] + "…[truncated]" : safe;
    }

    private static void RunRedactionSelfTest()
    {
        const string password = "dummy-P@ss;word";
        const string token = "dummy-access-token-value";
        const string code = "dummy-auth-code-value";
        const string connection = "Server=sql.khatadhari.com,14330;Database=WhatsBizERP_PROD;User ID=test_user;Password=\"dummy-P@ss;word\";Encrypt=True;TrustServerCertificate=False";
        var sample = $"Login failed. {connection}\naccess_token={token}\nauthorization_code={code}\nAppSecret=another-secret";
        using var capture = new StringWriter(CultureInfo.InvariantCulture);
        var originalOutput = Console.Out;
        string safe;
        try
        {
            Console.SetOut(capture);
            WriteSafeException(new InvalidOperationException(sample), connection);
            safe = capture.ToString();
        }
        finally { Console.SetOut(originalOutput); }
        var failedChecks = new List<string>();
        if (safe.Contains(password, StringComparison.Ordinal)) failedChecks.Add("password");
        if (safe.Contains(token, StringComparison.Ordinal)) failedChecks.Add("token");
        if (safe.Contains(code, StringComparison.Ordinal)) failedChecks.Add("authorization code");
        if (safe.Contains("another-secret", StringComparison.Ordinal)) failedChecks.Add("secret");
        if (safe.Contains("Server=", StringComparison.OrdinalIgnoreCase)) failedChecks.Add("connection string");
        if (safe.Contains("Password=", StringComparison.OrdinalIgnoreCase)) failedChecks.Add("password keyword");
        if (failedChecks.Count > 0) throw new InvalidOperationException($"Safe diagnostic redaction self-test failed for: {string.Join(", ", failedChecks)}.");
        Console.WriteLine("PASS: exception diagnostics redact passwords, full connection strings, tokens, secrets, and authorization codes.");
    }

    private static void InspectReadOnlyState(string connectionString)
    {
        const string sql = @"
SET NOCOUNT ON;
SELECT DB_NAME() AS DatabaseName,
       d.page_verify_option_desc AS PageVerify,
       d.target_recovery_time_in_seconds AS TargetRecoveryTime,
       (SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped=0) AS UserTables,
       (SELECT COUNT_BIG(*) FROM sys.objects WHERE is_ms_shipped=0) AS UserObjects,
       (SELECT COUNT_BIG(*) FROM sys.schemas WHERE name IN
         (N'audit',N'admin',N'commerce',N'core',N'dashboard',N'finance',N'gst',N'integration',N'inventory',N'loyalty',N'marketing',N'master',N'printing',N'purchase',N'reporting',N'sales')) AS WhatsBizApplicationSchemas,
       CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM sys.objects WHERE name=N'__RefactorLog') THEN 1 ELSE 0 END) AS HasRefactorLog
FROM sys.databases d WHERE d.database_id=DB_ID();
SELECT s.name AS SchemaName, COALESCE(o.type_desc,N'(EMPTY SCHEMA)') AS ObjectType, COUNT_BIG(o.object_id) AS ObjectCount
FROM sys.schemas s
LEFT JOIN sys.objects o ON o.schema_id=s.schema_id AND o.is_ms_shipped=0
WHERE s.name IN (N'audit',N'admin',N'commerce',N'core',N'dashboard',N'finance',N'gst',N'integration',N'inventory',N'loyalty',N'marketing',N'master',N'printing',N'purchase',N'reporting',N'sales')
GROUP BY s.name,o.type_desc ORDER BY s.name,o.type_desc;";
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = new SqlCommand(sql, connection) { CommandTimeout = 30 };
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Read-only state query returned no database metadata.");
        Console.WriteLine($"Database: {reader.GetString(0)}");
        Console.WriteLine($"PAGE_VERIFY: {reader.GetString(1)}");
        Console.WriteLine($"TARGET_RECOVERY_TIME: {reader.GetInt32(2)}");
        Console.WriteLine($"UserTables: {reader.GetInt64(3)}");
        Console.WriteLine($"UserObjects: {reader.GetInt64(4)}");
        Console.WriteLine($"WhatsBizApplicationSchemas: {ReadInt64(reader, 5)}");
        Console.WriteLine($"__RefactorLog exists: {reader.GetBoolean(6)}");
        if (!reader.NextResult()) return;
        Console.WriteLine("Application objects by schema and type:");
        while (reader.Read()) Console.WriteLine($"  {reader.GetString(0)} | {reader.GetString(1)} | {reader.GetInt64(2)}");
    }

    private static long ReadInt64(DbDataReader reader, int ordinal) => reader.GetFieldValue<long>(ordinal);

    private static void RunInspectStateTypeSelfTest()
    {
        var table = new DataTable();
        table.Columns.Add("CountBig", typeof(long));
        table.Rows.Add(0L);
        table.Rows.Add((long)int.MaxValue + 1L);
        using var reader = table.CreateDataReader();
        var actual = new List<long>();
        while (reader.Read()) actual.Add(ReadInt64(reader, 0));
        if (actual.Count != 2 || actual[0] != 0L || actual[1] != (long)int.MaxValue + 1L)
            throw new InvalidOperationException("Inspect-state Int64 count handling self-test failed.");
        Console.WriteLine("PASS: COUNT_BIG metadata values are read as Int64, including values greater than Int32.MaxValue.");
    }

    private static void ValidateConnection(string? connectionString, string database)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Production connection is not configured.");

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in builder.Keys)
            values[key.Replace(" ", string.Empty, StringComparison.Ordinal)] = Convert.ToString(builder[key], CultureInfo.InvariantCulture) ?? string.Empty;

        string Required(string key) => values.TryGetValue(key, out var value) ? value : string.Empty;
        var targetDatabase = FirstNonEmpty(Required("Database"), Required("InitialCatalog"));
        var server = FirstNonEmpty(Required("Server"), Required("DataSource"), Required("Address"), Required("Addr"), Required("NetworkAddress"));
        var encrypt = Required("Encrypt");
        var trust = Required("TrustServerCertificate");

        if (!string.Equals(database, ExpectedDatabase, StringComparison.Ordinal) ||
            !string.Equals(targetDatabase, ExpectedDatabase, StringComparison.Ordinal) ||
            !string.Equals(server, ExpectedServer, StringComparison.OrdinalIgnoreCase) ||
            !bool.TryParse(encrypt, out var encryptionEnabled) || !encryptionEnabled ||
            !bool.TryParse(trust, out var trustCertificate) || trustCertificate)
            throw new InvalidOperationException("Production target or strict TLS requirements are invalid.");
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static Arguments ParseArguments(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException("Invalid helper arguments.");
            values.Add(args[index][2..], args[++index]);
        }

        string Get(string name) => values.TryGetValue(name, out var value) ? value : string.Empty;
        var operation = Get("operation");
        var rawDacpac = Get("dacpac");
        var dacpac = string.IsNullOrWhiteSpace(rawDacpac) ? string.Empty : Path.GetFullPath(rawDacpac);
        var database = Get("database");
        var createDatabase = bool.TryParse(Get("create-database"), out var create) && create;
        var freshProduction = bool.TryParse(Get("fresh-production"), out var fresh) && fresh;
        var output = Get("output");
        var backupPath = Get("backup-path");

        if (operation == "validate" || operation == "inspect-state")
            return new Arguments(operation, string.Empty, string.IsNullOrEmpty(database) ? ExpectedDatabase : database, false, false, null, null);

        if (operation == "runtime-tls-probe")
        {
            var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "operation", "database" };
            if ((string.IsNullOrEmpty(database) ? ExpectedDatabase : database) != ExpectedDatabase ||
                values.Keys.Any(key => !allowedKeys.Contains(key)))
                throw new ArgumentException("Invalid runtime TLS probe arguments.");
            return new Arguments(operation, string.Empty, ExpectedDatabase, false, false, null, null);
        }

        if (operation is "verify-backup" or "restore-backup")
        {
            var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "operation", "database", "backup-path" };
            if (database != ExpectedDatabase || string.IsNullOrWhiteSpace(backupPath) || values.Keys.Any(key => !allowedKeys.Contains(key)))
                throw new ArgumentException("Invalid backup verification arguments.");
            var validatedBackupPath = ValidateBackupPath(backupPath);
            return new Arguments(operation, string.Empty, database, false, false, null, validatedBackupPath);
        }

        if (operation == "provision-runtime-user")
        {
            var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "operation", "database" };
            if (database != ExpectedDatabase || values.Keys.Any(key => !allowedKeys.Contains(key)))
                throw new ArgumentException("Invalid runtime-user provisioning arguments.");
            return new Arguments(operation, string.Empty, ExpectedDatabase, false, false, null, null);
        }

        if (operation is "test-redaction" or "test-inspect-state-types" or "test-verify-backup" or "test-restore-backup" or "test-provision-runtime-user")
            return new Arguments(operation, string.Empty, ExpectedDatabase, false, false, null, null);

        if (operation == "test-verify-backup")
            return new Arguments(operation, string.Empty, ExpectedDatabase, false, false, null, null);

        if (!fresh && !bool.TryParse(Get("fresh-production"), out _))
            throw new ArgumentException("Fresh production state must be supplied explicitly.");

        if (operation is not ("report" or "script" or "publish") || !File.Exists(dacpac) || database != ExpectedDatabase ||
            (operation is "report" or "script" && string.IsNullOrWhiteSpace(output)))
            throw new ArgumentException("Invalid or unsafe helper arguments.");

        return new Arguments(operation, dacpac, database, createDatabase, freshProduction,
            string.IsNullOrWhiteSpace(output) ? null : Path.GetFullPath(output), null);
    }

    private sealed record Arguments(string Operation, string Dacpac, string Database, bool CreateDatabase, bool FreshProduction, string? Output, string? BackupPath);
}
