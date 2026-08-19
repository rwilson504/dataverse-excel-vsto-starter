using System;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseAddIn.Ingestion
{
    internal static class BulkMessageSupport
    {
        /// <summary>
        /// Bulk messages are available for custom tables and many, but not all, standard
        /// tables — Account and Contact notably do not support them. Probe rather than assume.
        /// </summary>
        public static bool IsMessageAvailable(IOrganizationService service, string tableLogicalName, string messageName)
        {
            var query = new QueryExpression("sdkmessagefilter")
            {
                ColumnSet = new ColumnSet("sdkmessagefilterid"),
                TopCount = 1,
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
                    {
                        new ConditionExpression("primaryobjecttypecode", ConditionOperator.Equal, tableLogicalName)
                    }
                },
                LinkEntities =
                {
                    new LinkEntity("sdkmessagefilter", "sdkmessage", "sdkmessageid", "sdkmessageid", JoinOperator.Inner)
                    {
                        LinkCriteria = new FilterExpression(LogicalOperator.And)
                        {
                            Conditions = { new ConditionExpression("name", ConditionOperator.Equal, messageName) }
                        }
                    }
                }
            };

            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        public static string MessageNameFor(IngestionOperation operation)
        {
            switch (operation)
            {
                case IngestionOperation.Create: return "CreateMultiple";
                case IngestionOperation.Update: return "UpdateMultiple";
                case IngestionOperation.Upsert: return "UpsertMultiple";
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }

    internal static class ServiceProtection
    {
        // Documented service protection limit fault codes.
        private const int NumberOfRequestsExceeded = -2147015902; // 0x80072322
        private const int TimeLimitExceeded = -2147015903;        // 0x80072321
        private const int ConcurrentRequestsExceeded = -2147015898; // 0x80072326

        /// <summary>
        /// True for transient service protection faults. Dataverse usually supplies a
        /// Retry-After value; that is the most reliable signal, so it is checked first.
        /// </summary>
        public static bool IsThrottling(Exception exception, out TimeSpan retryAfter)
        {
            retryAfter = TimeSpan.Zero;

            if (!(exception is FaultException<OrganizationServiceFault> fault))
                return false;

            var detail = fault.Detail;
            if (detail == null) return false;

            if (detail.ErrorDetails != null &&
                detail.ErrorDetails.TryGetValue("Retry-After", out var value))
            {
                retryAfter = value is TimeSpan span
                    ? span
                    : TimeSpan.TryParse(value?.ToString(), out var parsed) ? parsed : TimeSpan.Zero;

                if (retryAfter > TimeSpan.Zero) return true;
            }

            switch (detail.ErrorCode)
            {
                case NumberOfRequestsExceeded:
                case TimeLimitExceeded:
                case ConcurrentRequestsExceeded:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/>, waiting out service protection limits. Throttling is
        /// expected at maximum throughput and is not an error.
        /// </summary>
        public static T Execute<T>(Func<T> action, int maxRetries, ref int throttledRetries, CancellationToken cancellationToken)
        {
            var attempt = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return action();
                }
                catch (Exception ex) when (IsThrottling(ex, out var retryAfter) && attempt < maxRetries)
                {
                    attempt++;
                    Interlocked.Increment(ref throttledRetries);

                    var wait = retryAfter > TimeSpan.Zero
                        ? retryAfter
                        : TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt)));

                    cancellationToken.WaitHandle.WaitOne(wait);
                }
            }
        }
    }
}
