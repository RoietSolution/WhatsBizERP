using FluentAssertions;
using Microsoft.Extensions.Logging;
using WhatsBiz.Infrastructure.Purchases;

namespace WhatsBiz.Tests.Purchases;

public sealed class PurchaseIntegrityDiagnosticsTests
{
    [Fact]
    public void LogsOriginalExceptionAndSafeOperationContext()
    {
        var logger = new CaptureLogger();
        var original = new InvalidOperationException("SQL integrity detail");
        var sourceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        PurchaseIntegrityDiagnostics.Log(logger, "Purchase", "purchase.Purchase_Post", sourceId, tenantId, 547, original);

        logger.Exception.Should().BeSameAs(original);
        logger.Message.Should().Contain("Purchase");
        logger.Message.Should().Contain("547");
        logger.Message.Should().Contain("purchase.Purchase_Post");
        logger.Message.Should().Contain(sourceId.ToString());
        logger.Message.Should().Contain(tenantId.ToString());
        logger.Message.Should().Contain("SQL integrity detail");
    }

    private sealed class CaptureLogger : ILogger
    {
        public Exception? Exception { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Exception = exception;
            Message = formatter(state, exception);
        }
    }
}
