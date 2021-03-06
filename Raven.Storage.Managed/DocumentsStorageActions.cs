//-----------------------------------------------------------------------
// <copyright file="DocumentsStorageActions.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using Raven.Database;
using Raven.Database.Exceptions;
using Raven.Database.Impl;
using Raven.Database.Json;
using Raven.Database.Plugins;
using Raven.Database.Storage;
using Raven.Http;
using Raven.Http.Exceptions;
using Raven.Storage.Managed.Impl;
using System.Linq;
using Raven.Database.Extensions;

namespace Raven.Storage.Managed
{
    public class DocumentsStorageActions : IDocumentStorageActions
    {
        private readonly TableStorage storage;
        private readonly ITransactionStorageActions transactionStorageActions;
        private readonly IUuidGenerator generator;
        private readonly IEnumerable<AbstractDocumentCodec> documentCodecs;

        public DocumentsStorageActions(TableStorage storage, ITransactionStorageActions transactionStorageActions, IUuidGenerator generator, IEnumerable<AbstractDocumentCodec> documentCodecs)
        {
            this.storage = storage;
            this.transactionStorageActions = transactionStorageActions;
            this.generator = generator;
            this.documentCodecs = documentCodecs;
        }

        public Tuple<int, int> FirstAndLastDocumentIds()
        {
            var lastOrDefault = storage.Documents["ById"].LastOrDefault();
            var last = 0;
            if (lastOrDefault != null)
                last = lastOrDefault.Value<int>("id");

            var firstOrDefault = storage.Documents["ById"].FirstOrDefault();
            var first = 0;
            if (firstOrDefault != null)
                first= firstOrDefault.Value<int>("id");
            return new Tuple<int, int>(first,last );
        }

        public IEnumerable<Tuple<JsonDocument, int>> DocumentsById(int startId, int endId)
        {
            var results = storage.Documents["ById"].SkipTo(new JObject{{"id", startId}})
                .TakeWhile(x=>x.Value<int>("id") <= endId);

            foreach (var result in results)
            {
                yield return new Tuple<JsonDocument, int>(
                    DocumentByKey(result.Value<string>("key"), null),
                    result.Value<int>("id")
                    );
            }
        }

        public IEnumerable<JsonDocument> GetDocumentsByReverseUpdateOrder(int start)
        {
            return storage.Documents["ByEtag"].SkipFromEnd(start)
                .Select(result => DocumentByKey(result.Value<string>("key"), null));
        }

        public IEnumerable<JsonDocument> GetDocumentsAfter(Guid etag)
        {
            return storage.Documents["ByEtag"].SkipAfter(new JObject {{"etag", etag.ToByteArray()}})
                .Select(result => DocumentByKey(result.Value<string>("key"), null));
        }

		public IEnumerable<JsonDocument> GetDocumentsWithIdStartingWith(string idPrefix, int start)
		{
			return storage.Documents["ByKey"].SkipAfter(new JObject {{"key", idPrefix}})
				.Skip(start)
				.TakeWhile(x => x.Value<string>("key").StartsWith(idPrefix))
				.Select(result => DocumentByKey(result.Value<string>("key"), null));
		}

		public long GetDocumentsCount()
        {
            return storage.Documents["ByKey"].Count;
        }

        public JsonDocument DocumentByKey(string key, TransactionInformation transactionInformation)
        {
        	return DocumentByKeyInternal(key, transactionInformation, (stream, metadata) => new JsonDocument
        	{
        		Metadata = metadata.Metadata,
        		Etag = metadata.Etag,
        		Key = metadata.Key,
        		LastModified = metadata.LastModified,
        		NonAuthoritiveInformation = metadata.NonAuthoritiveInformation,
				DataAsJson = ReadDocument(stream, metadata)
        	});
        }

		public JsonDocumentMetadata DocumentMetadataByKey(string key, TransactionInformation transactionInformation)
		{
			return DocumentByKeyInternal(key, transactionInformation, (stream, metadata) => metadata);
		}


    	private T DocumentByKeyInternal<T>(string key, TransactionInformation transactionInformation, Func<Tuple<MemoryStream, JObject>, JsonDocumentMetadata, T> createResult)
			where T : class
    	{
    		JObject metadata = null;
            
    		var resultInTx = storage.DocumentsModifiedByTransactions.Read(new JObject { { "key", key } });
    		if (transactionInformation != null && resultInTx != null)
    		{
    			if(new Guid(resultInTx.Key.Value<byte[]>("txId")) == transactionInformation.Id)
    			{
    				if (resultInTx.Key.Value<bool>("deleted"))
    					return null;

    				var txEtag = new Guid(resultInTx.Key.Value<byte[]>("etag"));
    				Tuple<MemoryStream, JObject> resultTx= null;
					if (resultInTx.Position != -1)
					{
						resultTx = ReadMetadata(key, txEtag, resultInTx.Data, out metadata);
					}
					return createResult(resultTx, new JsonDocumentMetadata
    				{
    					Key = key,
    					Etag = txEtag,
    					Metadata = metadata,
    					LastModified = resultInTx.Key.Value<DateTime>("modified"),
    				});
    			}
    		}

    		var readResult = storage.Documents.Read(new JObject{{"key", key}});
    		if (readResult == null)
    			return null;

    		var etag = new Guid(readResult.Key.Value<byte[]>("etag"));
    		var result = ReadMetadata(key, etag, readResult.Data, out metadata);
			return createResult(result, new JsonDocumentMetadata
    		{
    			Key = key,
    			Etag = etag,
    			Metadata = metadata,
    			LastModified = readResult.Key.Value<DateTime>("modified"),
    			NonAuthoritiveInformation = resultInTx != null
    		});
    	}

		private Tuple<MemoryStream, JObject> ReadMetadata(string key, Guid etag, Func<byte[]> getData, out JObject metadata)
        {
        	var cachedDocument = storage.GetCachedDocument(key, etag);
        	if (cachedDocument != null)
        	{
        		metadata = cachedDocument.Item1;
        		return Tuple.Create<MemoryStream, JObject>(null, cachedDocument.Item2);
        	}

        	var buffer = getData();
        	var memoryStream = new MemoryStream(buffer, 0, buffer.Length, true , true);

        	metadata = memoryStream.ToJObject();

			return Tuple.Create<MemoryStream, JObject>(memoryStream, null);
        }

    	private JObject ReadDocument(Tuple<MemoryStream,JObject> stream, JsonDocumentMetadata metadata)
    	{
			if (stream.Item2 != null)
				return stream.Item2;

			var memoryStream = stream.Item1;
			if (documentCodecs.Count() > 0)
    		{
    			byte[] buffer = memoryStream.GetBuffer();
				var metadataCopy = new JObject(metadata.Metadata);
				var dataBuffer = new byte[memoryStream.Length - memoryStream.Position];
				Buffer.BlockCopy(buffer, (int)memoryStream.Position, dataBuffer, 0,
    			                 dataBuffer.Length);
    			documentCodecs.Aggregate(dataBuffer, (bytes, codec) => codec.Decode(metadata.Key, metadataCopy, bytes));
				memoryStream = new MemoryStream(dataBuffer);
    		}

			var result = memoryStream.ToJObject();

			storage.SetCachedDocument(metadata.Key, metadata.Etag, Tuple.Create(new JObject(metadata.Metadata), new JObject(result)));

    		return result;
    	}

    	public bool DeleteDocument(string key, Guid? etag, out JObject metadata)
        {
            AssertValidEtag(key, etag, "DELETE", null);

            metadata = null;
            var readResult = storage.Documents.Read(new JObject{{"key",key}});
            if (readResult == null)
                return false;

            metadata = readResult.Data().ToJObject();

            storage.Documents.Remove(new JObject
            {
                {"key", key},
            });

            return true;
        }

        public Guid AddDocument(string key, Guid? etag, JObject data, JObject metadata)
        {
            AssertValidEtag(key, etag, "PUT", null);

            var ms = new MemoryStream();

            metadata.WriteTo(ms);

            var bytes = documentCodecs.Aggregate(data.ToBytes(), (current, codec) => codec.Encode(key, data, metadata, current));

            ms.Write(bytes, 0, bytes.Length);

            var newEtag = generator.CreateSequentialUuid();
            storage.Documents.Put(new JObject
            {
                {"key", key},
                {"etag", newEtag.ToByteArray()},
                {"modified", DateTime.UtcNow},
                {"id", GetNextDocumentId()},
                {"entityName", metadata.Value<string>("Raven-Entity-Name")}
            },ms.ToArray());

            return newEtag;
        }

        private int lastGeneratedId;
        private int GetNextDocumentId()
        {
            if (lastGeneratedId > 0)
                return ++lastGeneratedId;

            var lastOrefaultKeyById = storage.Documents["ById"].LastOrDefault();
            int id = 1;
            if (lastOrefaultKeyById != null)
                id = lastOrefaultKeyById.Value<int>("id") + 1;
            lastGeneratedId = id;
            return id;
        }

        private void AssertValidEtag(string key, Guid? etag, string op, TransactionInformation transactionInformation)
        {
            var readResult = storage.Documents.Read(new JObject
            {
                {"key", key},
            });

            if (readResult != null)
            {
                StorageHelper.AssertNotModifiedByAnotherTransaction(storage, transactionStorageActions, key, readResult, transactionInformation);

                if (etag != null)
                {
                    var existingEtag = new Guid(readResult.Key.Value<byte[]>("etag"));
                    if (existingEtag != etag)
                    {
                        throw new ConcurrencyException(op + " attempted on document '" + key +
                                                       "' using a non current etag")
                        {
                            ActualETag = etag.Value,
                            ExpectedETag = existingEtag
                        };
                    }
                }
            }
            else
            {
                readResult = storage.DocumentsModifiedByTransactions.Read(new JObject
                {
                    {"key", key}
                });
                StorageHelper.AssertNotModifiedByAnotherTransaction(storage, transactionStorageActions, key, readResult, transactionInformation);

            }
        }

    }
}