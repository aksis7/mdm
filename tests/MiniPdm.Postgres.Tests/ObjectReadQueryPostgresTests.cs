using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MiniPdm.Domain.Objects;
using MiniPdm.Domain.Versions;
using MiniPdm.Storage;
using MiniPdm.Modules.Objects.Services;
using Npgsql;
using Xunit;

namespace MiniPdm.Postgres.Tests;

/// <summary>
/// Проверяет поиск объектов и чтение карточек в PostgreSQL.
/// </summary>
public sealed class ObjectReadQueryPostgresTests
{
    private const string ConnectionVariable = "PDM_TEST_POSTGRES_CONNECTION";

    /// <summary>
    /// Проверяет регистронезависимый поиск кириллицы, буквальное трактование подстановочных символов и получение карточки одним запросом.
    /// </summary>
    /// <returns>Завершение проверки подтверждает ожидаемое поведение; нарушение ожиданий приводит к ошибке утверждения.</returns>
    [Fact]
    public async Task ILike_is_case_insensitive_for_cyrillic_and_treats_wildcards_as_literals_and_card_is_one_query()
    {
        await using var fixture = await Fixture.OpenAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var standardName = $"Корпус {marker} 50%_\\X";
        var item = new PdmObject
        {
            Type = PdmObjectType.StandardPart,
            NormalizedName = standardName,
            StandardName = standardName
        };
        await fixture.SaveObjectsAsync(item);
        var version = new ObjectVersion { ObjectId = item.Id, Version = 1, State = VersionState.Approved, Mass = 0.25m };
        await fixture.SaveVersionsAsync(version);
        await fixture.SetCurrentAsync(item, version);
        var control = new PdmObject
        {
            Type = PdmObjectType.StandardPart,
            NormalizedName = $"Прокладка {marker} 50AB\\X",
            StandardName = $"Прокладка {marker} 50AB\\X"
        };
        await fixture.SaveObjectsAsync(control);
        var controlVersion = new ObjectVersion { ObjectId = control.Id, Version = 1, State = VersionState.Approved, Mass = 0.1m };
        await fixture.SaveVersionsAsync(controlVersion);
        await fixture.SetCurrentAsync(control, controlVersion);
        var query = new ObjectReadService(fixture.Context);

        foreach (var term in new[] { $"корпус {marker}", $"{marker} 50%_", $"{marker} 50%_\\X" })
        {
            fixture.Commands.Reset();
            var page = await query.SearchAsync(term, 0, 20, CancellationToken.None);
            Assert.Equal(1, fixture.Commands.Count);
            Assert.Equal(item.Id, Assert.Single(page.Items).Id);
        }

        fixture.Commands.Reset();
        var publicPage = await query.SearchObjectsAsync($"корпус {marker}", 0, 20, CancellationToken.None);
        Assert.Equal(1, fixture.Commands.Count);
        Assert.Equal(item.Id, Assert.Single(publicPage.Items).Id);
        Assert.Equal("StandardPart", publicPage.Items[0].Type);
        Assert.Equal(standardName, publicPage.Items[0].Name);

        var cancelled = new ObjectVersion
        {
            ObjectId = item.Id,
            Version = 2,
            State = VersionState.Cancelled,
            Name = "Historical cancelled body",
            Material = "Historical alloy",
            Mass = 0.5m,
            SourceReference = "history/source.step"
        };
        fixture.Context.Versions.Add(cancelled);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        fixture.Commands.Reset();
        var card = await query.GetAsync(item.Id, 2, CancellationToken.None);
        Assert.Equal(1, fixture.Commands.Count);
        Assert.NotNull(card);
        Assert.Equal(standardName, card!.StandardName);
        Assert.Equal(version.Id, card.CurrentVersionId);
        Assert.Equal(2, card.SelectedVersion!.Version);
        Assert.Equal(VersionState.Cancelled, card.SelectedVersion.State);
        Assert.Equal("Historical cancelled body", card.SelectedVersion.Name);
        Assert.Equal("Historical alloy", card.SelectedVersion.Material);
        Assert.Equal(0.5m, card.SelectedVersion.Mass);
        Assert.Equal("history/source.step", card.SelectedVersion.SourceReference);
        Assert.Equal(new[] { 2, 1 }, card.Versions.Select(v => v.Version));
        Assert.Equal(card.CurrentVersionId, card.Versions.Single(v => v.Version == 1).Id);
        Assert.NotEqual(card.CurrentVersionId, card.Versions.Single(v => v.Version == 2).Id);
        Assert.Empty(fixture.Context.ChangeTracker.Entries());

        fixture.Commands.Reset();
        var publicCard = await query.GetObjectAsync(item.Id, 2, CancellationToken.None);
        Assert.Equal(1, fixture.Commands.Count);
        Assert.Equal(standardName, publicCard!.Name);
        Assert.Equal(standardName, publicCard.SelectedVersion!.Name);
        Assert.False(publicCard.SelectedVersion.IsCurrent);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;

        private Fixture(NpgsqlConnection connection, NpgsqlTransaction transaction, PdmDbContext context, CommandCounter commands)
        {
            _connection = connection;
            _transaction = transaction;
            Context = context;
            Commands = commands;
        }

        /// <summary>
        /// Контекст базы данных, используемый тестовой фикстурой.
        /// </summary>
        public PdmDbContext Context
        {
            get;
        }
        /// <summary>
        /// Перехватчик, учитывающий выполненные SQL-команды.
        /// </summary>
        public CommandCounter Commands
        {
            get;
        }

        /// <summary>
        /// Открывает транзакционную фикстуру для изолированной проверки PostgreSQL.
        /// </summary>
        /// <returns>Асинхронный результат операции и данные, полученные в результате её выполнения.</returns>
        public static async Task<Fixture> OpenAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException($"Set {ConnectionVariable} to a dedicated, already-migrated PostgreSQL test database before running these integration tests.");

            var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            try
            {
                var transaction = await connection.BeginTransactionAsync();
                var commands = new CommandCounter();
                var options = new DbContextOptionsBuilder<PdmDbContext>().UseNpgsql(connection).AddInterceptors(commands).Options;
                var context = new PdmDbContext(options);
                await context.Database.UseTransactionAsync(transaction);
                return new Fixture(connection, transaction, context, commands);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        /// <summary>
        /// Добавляет объекты в контекст фикстуры и сохраняет изменения.
        /// </summary>
        /// <param name="objects">Объекты для добавления в контекст фикстуры.</param>
        /// <returns>Задача завершается после добавления тестовых данных в базу.</returns>
        public async Task SaveObjectsAsync(params PdmObject[] objects)
        {
            Context.Objects.AddRange(objects);
            await Context.SaveChangesAsync();
        }

        /// <summary>
        /// Добавляет версии в контекст фикстуры и сохраняет изменения.
        /// </summary>
        /// <param name="versions">Версии для добавления в контекст фикстуры.</param>
        /// <returns>Задача завершается после сохранения версии объекта.</returns>
        public async Task SaveVersionsAsync(params ObjectVersion[] versions)
        {
            Context.Versions.AddRange(versions);
            await Context.SaveChangesAsync();
        }

        /// <summary>
        /// Устанавливает текущую версию объекта и сохраняет изменения.
        /// </summary>
        /// <param name="item">Объект, которому назначается текущая версия.</param>
        /// <param name="version">Версия, назначаемая текущей для объекта.</param>
        /// <returns>Задача завершается после установки текущей версии объекта.</returns>
        public async Task SetCurrentAsync(PdmObject item, ObjectVersion version)
        {
            item.CurrentVersionId = version.Id;
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
        }

        /// <summary>
        /// Освобождает контекст и соединение после отката тестовой транзакции.
        /// </summary>
        /// <returns>Задача завершается после освобождения ресурсов тестовой фикстуры.</returns>
        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;
        /// <summary>
        /// Число выполненных SQL-команд, зарегистрированных перехватчиком.
        /// </summary>
        public int Count => Volatile.Read(ref _count);
        /// <summary>
        /// Сбрасывает счётчик выполненных SQL-команд.
        /// </summary>
        public void Reset() => Interlocked.Exchange(ref _count, 0);

        /// <inheritdoc/>
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return result;
        }

        /// <inheritdoc/>
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return ValueTask.FromResult(result);
        }
    }
}
