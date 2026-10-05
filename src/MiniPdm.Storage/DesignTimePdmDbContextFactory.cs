using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MiniPdm.Storage;

/// <summary>
/// Создаёт контекст базы данных для команд EF Core во время разработки.
/// </summary>
public sealed class DesignTimePdmDbContextFactory : IDesignTimeDbContextFactory<PdmDbContext>
{
    /// <summary>
    ///     Создаёт контекст базы данных для команд EF Core во время разработки.
    /// </summary>
    /// <param name="args">
    ///     Аргументы инструмента разработки EF Core.
    /// </param>
    /// <returns>
    ///     Контекст, настроенный через <c>PDM_CONNECTION_STRING</c>.
    /// </returns>
    public PdmDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PDM_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set PDM_CONNECTION_STRING to create or apply a database migration.");
        var options = new DbContextOptionsBuilder<PdmDbContext>().UseNpgsql(connectionString).Options;
        return new PdmDbContext(options);
    }
}
