using System.Text.RegularExpressions;
using Constia.Application.Usuarios;
using Constia.Domain;
using Constia.Infrastructure;
using Constia.Infrastructure.Security;
using Constia.Infrastructure.Usuarios;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Constia.API.Tests;

    internal sealed class EmailVerificationSqlDatabase : IAsyncDisposable
    {
        private const string EnabledVariable="CONSTIA_AUTHEMAIL01B_SQLSERVER_TESTS";
        private const string ConnectionVariable="CONSTIA_AUTHEMAIL01B_SQLSERVER_CONNECTION";
        private const string ContainerVariable="CONSTIA_AUTHEMAIL01B_CONTAINER_ID";
        private static bool IsEnabled() => Environment.GetEnvironmentVariable("GITHUB_ACTIONS")=="true" && Environment.GetEnvironmentVariable(EnabledVariable)=="true" && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable));
        private readonly string _adminConnection;
        private readonly string _name;
        public string ConnectionString { get; }

        private EmailVerificationSqlDatabase(string admin, string name)
        {
            _adminConnection = admin;
            _name = name;
            ConnectionString = new SqlConnectionStringBuilder(admin) { InitialCatalog = name }.ConnectionString;
        }

        internal ConstiaDbContext CreateContext(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<ConstiaDbContext>()
            .UseSqlServer(ConnectionString).AddInterceptors(interceptors).Options);

        internal static async Task<EmailVerificationSqlDatabase> CreateAsync()
        {
            var admin = await VerifyAdminAsync();
            var database = new EmailVerificationSqlDatabase(admin, $"Constia_AuthEmail01B_{Guid.NewGuid():N}");
            await using var connection = new SqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{database._name}];", connection);
            await command.ExecuteNonQueryAsync();
            return database;
        }

        public async ValueTask DisposeAsync()
        {
            if (!Regex.IsMatch(_name, "^Constia_AuthEmail01B_[0-9a-f]{32}$")
                || new SqlConnectionStringBuilder(ConnectionString).InitialCatalog != _name)
                throw new InvalidOperationException("Refusing cleanup of a non-AUTH-EMAIL-01-B database.");
            var admin = await VerifyAdminAsync();
            if (admin != _adminConnection)
                throw new InvalidOperationException("The disposable SQL destination changed.");
            using (var ownPool = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(ownPool);
            await using var connection = new SqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}];", connection);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<string> VerifyAdminAsync()
        {
            if (!IsEnabled()
                || Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
                throw new InvalidOperationException("AUTH-EMAIL-01-B requires its explicitly enabled disposable GitHub Actions service.");
            var container = Environment.GetEnvironmentVariable(ContainerVariable) ?? "";
            if (!Regex.IsMatch(container, "^[0-9a-f]{64}$"))
                throw new InvalidOperationException("Missing disposable SQL service container identity.");
            var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ConnectionVariable));
            if (builder.DataSource != "127.0.0.1,1433" || builder.InitialCatalog != "master"
                || builder.UserID != "sa" || string.IsNullOrWhiteSpace(builder.Password))
                throw new InvalidOperationException("Only the disposable runner SQL endpoint is allowed.");
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT CONVERT(int,SERVERPROPERTY('ProductMajorVersion')),CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'));", connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            if (reader.GetInt32(0) != 16 || reader.GetString(1) != container[..12])
                throw new InvalidOperationException("SQL Server 2022 service identity does not match the disposable CI container.");
            return builder.ConnectionString;
        }
    }
