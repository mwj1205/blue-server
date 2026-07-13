using blueServer.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

const int maxAttempts = 10;
var connectionString = Environment.GetEnvironmentVariable(
    "ConnectionStrings__Default");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "Database connection string is not configured.");
    return 1;
}

using var cancellationTokenSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

for (var attempt = 1; attempt <= maxAttempts; attempt++)
{
    try
    {
        var options = new DbContextOptionsBuilder<GameDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var dbContext = new GameDbContext(options);

        Console.WriteLine(
            $"Applying database migrations. Attempt {attempt}/{maxAttempts}.");

        await dbContext.Database.MigrateAsync(
            cancellationTokenSource.Token);

        Console.WriteLine("Database migrations completed.");
        return 0;
    }
    catch (OperationCanceledException)
        when (cancellationTokenSource.IsCancellationRequested)
    {
        Console.Error.WriteLine("Database migration was cancelled.");
        return 1;
    }
    catch (Exception exception)
        when (attempt < maxAttempts && IsTransient(exception))
    {
        Console.Error.WriteLine(
            $"Database migration attempt failed with {exception.GetType().Name}. Retrying.");

        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(2),
                cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
            when (cancellationTokenSource.IsCancellationRequested)
        {
            Console.Error.WriteLine("Database migration was cancelled.");
            return 1;
        }
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"Database migration failed with {exception.GetType().Name}: {exception.Message}");
        return 1;
    }
}

return 1;

static bool IsTransient(Exception exception)
{
    return exception is TimeoutException ||
        exception is NpgsqlException { IsTransient: true } ||
        exception.InnerException is not null &&
        IsTransient(exception.InnerException);
}
