using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseAddIn.Ingestion.Tests
{
    /// <summary>
    /// Records every request and lets a test decide the response. Shared collections are
    /// concurrent because the engine runs chunks in parallel.
    /// </summary>
    internal sealed class FakeOrganizationService : IOrganizationService
    {
        private readonly FakeDataverse _state;

        public FakeOrganizationService(FakeDataverse state) => _state = state;

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            _state.Requests.Add(request);
            return _state.Respond(request);
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            // Only used by the CreateMultiple availability probe.
            var results = new EntityCollection();

            if (_state.BulkMessagesSupported)
                results.Entities.Add(new Entity("sdkmessagefilter", Guid.NewGuid()));

            return results;
        }

        public Guid Create(Entity entity) => throw new NotImplementedException();
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotImplementedException();
        public void Update(Entity entity) => throw new NotImplementedException();
        public void Delete(string entityName, Guid id) => throw new NotImplementedException();
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotImplementedException();
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotImplementedException();
    }

    internal sealed class FakeDataverse
    {
        public ConcurrentBag<OrganizationRequest> Requests { get; } = new ConcurrentBag<OrganizationRequest>();

        public bool BulkMessagesSupported { get; set; } = true;

        /// <summary>Set to make CreateMultiple throw, simulating an all-or-nothing rollback.</summary>
        public Func<CreateMultipleRequest, Exception> FailBulk { get; set; }

        /// <summary>Set to make an individual Create fail, keyed on the record's name column.</summary>
        public Func<Entity, Exception> FailSingle { get; set; }

        public Func<IOrganizationService> Factory => () => new FakeOrganizationService(this);

        public IEnumerable<OrganizationRequest> RequestsOfType<T>() => Requests.Where(r => r is T);

        public OrganizationResponse Respond(OrganizationRequest request)
        {
            switch (request)
            {
                case CreateMultipleRequest bulk:
                {
                    var failure = FailBulk?.Invoke(bulk);
                    if (failure != null) throw failure;

                    var response = new CreateMultipleResponse();
                    response.Results["Ids"] = bulk.Targets.Entities.Select(_ => Guid.NewGuid()).ToArray();
                    return response;
                }

                case CreateRequest single:
                {
                    var failure = FailSingle?.Invoke(single.Target);
                    if (failure != null) throw failure;

                    var response = new CreateResponse();
                    response.Results["id"] = Guid.NewGuid();
                    return response;
                }

                case ExecuteMultipleRequest batch:
                {
                    var items = new ExecuteMultipleResponseItemCollection();

                    for (var i = 0; i < batch.Requests.Count; i++)
                    {
                        var inner = (CreateRequest)batch.Requests[i];
                        var failure = FailSingle?.Invoke(inner.Target);

                        var item = new ExecuteMultipleResponseItem { RequestIndex = i };

                        if (failure != null)
                        {
                            item.Fault = new OrganizationServiceFault { Message = failure.Message };
                        }
                        else
                        {
                            var created = new CreateResponse();
                            created.Results["id"] = Guid.NewGuid();
                            item.Response = created;
                        }

                        items.Add(item);
                    }

                    var response = new ExecuteMultipleResponse();
                    response.Results["Responses"] = items;
                    return response;
                }

                default:
                    throw new NotSupportedException(request.RequestName);
            }
        }
    }
}
