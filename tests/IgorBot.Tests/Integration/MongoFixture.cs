using EphemeralMongo;

using MongoDB.Driver;
using MongoDB.Entities;

namespace IgorBot.Tests.Integration;

/// <summary>
///     xUnit collection fixture that boots a single <see cref="IMongoRunner" /> for the entire
///     integration-test assembly. Individual test classes obtain an isolated <see cref="DB" />
///     instance by calling <see cref="CreateDatabaseAsync" /> with a unique name.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private IMongoRunner? _runner;

    /// <summary>The connection string pointing at the embedded mongod process.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _runner = await MongoRunner.RunAsync(new MongoRunnerOptions
        {
            // Quiet startup; binary is downloaded to the OS user cache on first run.
            StandardErrorLogger = _ => { }
        });

        ConnectionString = _runner.ConnectionString;
    }

    public Task DisposeAsync()
    {
        _runner?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Creates and returns a <see cref="DB" /> instance backed by a freshly-named
    ///     database so each test class gets full isolation.
    /// </summary>
    public async Task<DB> CreateDatabaseAsync(string name) =>
        await DB.InitAsync(name, MongoClientSettings.FromConnectionString(ConnectionString));
}

/// <summary>Marks test classes that share the <see cref="MongoFixture" /> instance.</summary>
[Xunit.CollectionDefinition("Mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>;
