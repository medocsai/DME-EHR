using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services;

/// <summary>
/// Background service that processes due installment payments daily.
/// Charges saved payment methods for installments where DueDate &lt;= today.
/// </summary>
public class InstallmentProcessorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InstallmentProcessorBackgroundService> _logger;

    public InstallmentProcessorBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<InstallmentProcessorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Installment Processor Background Service started");

        // Initial delay to let the application fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var installmentService = scope.ServiceProvider.GetRequiredService<IInstallmentService>();
                await installmentService.ProcessDueInstallmentsAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in installment processing cycle");
            }

            // Run every 60 minutes
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Installment Processor Background Service stopped");
    }
}
