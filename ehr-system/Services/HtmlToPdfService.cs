using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace EHR.Services
{
    public interface IHtmlToPdfService
    {
        /// <summary>
        /// Generates a PDF from HTML content, rendering it exactly as it appears in a browser.
        /// </summary>
        /// <param name="htmlContent">The complete HTML document to render</param>
        /// <returns>PDF as byte array</returns>
        Task<byte[]> GeneratePdfFromHtmlAsync(string htmlContent);
    }

    public class HtmlToPdfService : IHtmlToPdfService
    {
        private readonly ILogger<HtmlToPdfService> _logger;
        private readonly IConfiguration _configuration;
        private static bool _browserReady = false;
        private static string? _chromePath = null;
        private static readonly SemaphoreSlim _initLock = new(1, 1);

        public HtmlToPdfService(ILogger<HtmlToPdfService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<byte[]> GeneratePdfFromHtmlAsync(string htmlContent)
        {
            try
            {
                // Ensure browser is ready (either pre-installed path or download)
                await EnsureBrowserReadyAsync();

                // Launch headless browser
                var launchOptions = new LaunchOptions
                {
                    Headless = true,
                    Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" }
                };

                // Use pre-installed Chrome path if available
                if (!string.IsNullOrEmpty(_chromePath))
                {
                    launchOptions.ExecutablePath = _chromePath;
                }

                await using var browser = await Puppeteer.LaunchAsync(launchOptions);

                await using var page = await browser.NewPageAsync();

                // SSRF defense: block all external resource fetches. Without this,
                // a clinical note PDF containing <img src="http://169.254.169.254/...">
                // or <iframe src="http://internal-host/..."> would force the headless
                // Chrome to fetch internal-network URLs (cloud metadata services,
                // internal admin panels) and could exfiltrate PHI to attacker-controlled
                // hosts. We only permit data:/about:/blob: URIs — anything that needs
                // to appear in the PDF must be embedded as base64.
                await page.SetRequestInterceptionAsync(true);
                page.Request += (sender, e) =>
                {
                    var url = e.Request.Url ?? string.Empty;
                    if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                        || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
                        || url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = e.Request.ContinueAsync();
                    }
                    else
                    {
                        _logger.LogWarning("PDF generation blocked external resource fetch: {Url}", url);
                        _ = e.Request.AbortAsync();
                    }
                };

                // Set content and wait for it to load
                await page.SetContentAsync(htmlContent, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.Load, WaitUntilNavigation.DOMContentLoaded }
                });

                // Generate PDF with print-friendly settings
                var pdfBytes = await page.PdfDataAsync(new PdfOptions
                {
                    Format = PaperFormat.Letter,
                    PrintBackground = true,
                    MarginOptions = new MarginOptions
                    {
                        Top = "0.5in",
                        Bottom = "0.5in",
                        Left = "0.5in",
                        Right = "0.5in"
                    }
                });

                _logger.LogInformation("Generated PDF from HTML, size: {Size} bytes", pdfBytes.Length);
                return pdfBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate PDF from HTML");
                throw;
            }
        }

        /// <summary>
        /// Ensures browser is ready - either uses pre-installed Chrome path from config,
        /// or downloads Chromium on first use.
        /// </summary>
        private async Task EnsureBrowserReadyAsync()
        {
            if (_browserReady) return;

            await _initLock.WaitAsync();
            try
            {
                if (!_browserReady)
                {
                    // Check for pre-installed Chrome path in configuration
                    var configuredPath = _configuration["PdfGeneration:ChromePath"];

                    if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
                    {
                        _chromePath = configuredPath;
                        _logger.LogInformation("Using pre-installed Chrome at: {Path}", _chromePath);
                    }
                    else
                    {
                        // Fallback: Try to download (works on localhost or servers with internet)
                        _logger.LogInformation("Downloading Chromium browser for PDF generation...");
                        try
                        {
                            var browserFetcher = new BrowserFetcher();
                            var installedBrowser = await browserFetcher.DownloadAsync();
                            _chromePath = installedBrowser.GetExecutablePath();
                            _logger.LogInformation("Chromium downloaded to: {Path}", _chromePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to download Chrome. Please configure 'PdfGeneration:ChromePath' in appsettings.json with the path to a pre-installed Chrome executable.");
                            throw new InvalidOperationException(
                                "Failed to download Chrome for PDF generation. On servers without internet access, " +
                                "please download Chrome manually and set 'PdfGeneration:ChromePath' in appsettings.json. " +
                                "Download from: https://storage.googleapis.com/chrome-for-testing-public/124.0.6367.60/win64/chrome-win64.zip", ex);
                        }
                    }

                    _browserReady = true;
                }
            }
            finally
            {
                _initLock.Release();
            }
        }
    }
}
