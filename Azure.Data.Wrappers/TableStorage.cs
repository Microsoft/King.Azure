﻿namespace Azure.Data.Wrappers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Azure.Data.Wrappers.Sanitization;
    using Azure.Data.Wrappers.Sanitization.Providers;
    using Microsoft.WindowsAzure.Storage;
    using Microsoft.WindowsAzure.Storage.RetryPolicies;
    using Microsoft.WindowsAzure.Storage.Table;

    /// <summary>
    /// Table Storage
    /// </summary>
    public class TableStorage : AzureStorage, ITableStorage
    {
        #region Members
        /// <summary>
        /// Partition Key
        /// </summary>
        public const string PartitionKey = "PartitionKey";

        /// <summary>
        /// Row Key
        /// </summary>
        public const string RowKey = "RowKey";

        /// <summary>
        /// Timestamp
        /// </summary>
        public const string Timestamp = "Timestamp";

        /// <summary>
        /// ETag
        /// </summary>
        public const string ETag = "ETag";

        /// <summary>
        /// Maximum Insert Batch
        /// </summary>
        public const int MaimumxInsertBatch = 100;

        /// <summary>
        /// Table Client
        /// </summary>
        private readonly CloudTableClient client;

        /// <summary>
        /// Table
        /// </summary>
        private readonly CloudTable reference;
        #endregion

        #region Constructors
        /// <summary>
        /// Table Storage
        /// </summary>
        /// <param name="tableName">Table Name</param>
        /// <param name="connectionString">Connection String</param>
        /// <param name="location">Location Mode</param>
        public TableStorage(string tableName, string connectionString, LocationMode location = LocationMode.PrimaryThenSecondary)
            : this(tableName, CloudStorageAccount.Parse(connectionString), location)
        {
        }

        /// <summary>
        /// Table Storage
        /// </summary>
        /// <param name="tableName">Table Name</param>
        /// <param name="account">Storage Account</param>
        /// <param name="location">Location Mode</param>
        public TableStorage(string tableName, CloudStorageAccount account, LocationMode location = LocationMode.PrimaryThenSecondary)
            : base(account)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                throw new ArgumentException("tableName");
            }

            this.client = base.Account.CreateCloudTableClient();
            this.client.DefaultRequestOptions.LocationMode = location;

            this.reference = client.GetTableReference(tableName);
        }
        #endregion

        #region Properties
        /// <summary>
        /// Table Name
        /// </summary>
        public virtual string Name
        {
            get
            {
                return this.reference.Name;
            }
        }

        /// <summary>
        /// Table Client
        /// </summary>
        public virtual CloudTableClient Client
        {
            get
            {
                return this.client;
            }
        }

        /// <summary>
        /// Table
        /// </summary>
        public virtual CloudTable Reference
        {
            get
            {
                return this.reference;
            }
        }
        #endregion

        #region Create Table
        /// <summary>
        /// Create If Not Exists
        /// </summary>
        /// <returns></returns>
        public virtual async Task<bool> CreateIfNotExists()
        {
            return await this.reference.CreateIfNotExistsAsync();
        }

        /// <summary>
        /// Create Table
        /// </summary>
        /// <param name="tableName">Table Name</param>
        public virtual async Task<bool> Create()
        {
            return await this.reference.CreateIfNotExistsAsync();
        }
        #endregion

        #region Delete
        /// <summary>
        /// Delete Table
        /// </summary>
        /// <param name="tableName"></param>
        public virtual async Task Delete()
        {
            await this.reference.DeleteAsync();
        }

        /// <summary>
        /// Delete By Partition
        /// </summary>
        /// <param name="partitionKey">Partition Key</param>
        /// <returns>Task</returns>
        public virtual async Task DeleteByPartition(string partitionKey)
        {
            var entities = await this.QueryByPartition<TableEntity>(partitionKey);
            if (null != entities && entities.Any())
            {
                await this.Delete(entities);
            }
        }

        /// <summary>
        /// Delete By Row
        /// </summary>
        /// <param name="rowKey">Row Key</param>
        /// <returns>Task</returns>
        public virtual async Task DeleteByRow(string rowKey)
        {
            var entities = await this.QueryByRow<TableEntity>(rowKey);
            if (null != entities && entities.Any())
            {
                foreach (var entity in entities)
                {
                    await this.Delete(entity);
                }
            }
        }

        /// <summary>
        /// Delete By Partition and Row 
        /// </summary>
        /// <param name="partitionKey">Partition Key</param>
        /// <param name="rowKey">Row Key</param>
        /// <returns>Task</returns>
        public virtual async Task DeleteByPartitionAndRow(string partitionKey, string rowKey)
        {
            var entity = await this.QueryByPartitionAndRow<TableEntity>(partitionKey, rowKey);

            if (null != entity)
            {
                await this.Delete(entity);
            }
        }

        /// <summary>
        /// Delete Entity
        /// </summary>
        /// <param name="entity">Entity</param>
        /// <returns>Task</returns>
        public virtual async Task<TableResult> Delete(ITableEntity entity)
        {
            if (null == entity)
            {
                throw new ArgumentNullException("entity");
            }

            return await this.reference.ExecuteAsync(TableOperation.Delete(entity));
        }

        /// <summary>
        /// Delete Entities
        /// </summary>
        /// <param name="entities">Entities</param>
        /// <returns>Table Results</returns>
        public virtual async Task<IEnumerable<TableResult>> Delete(IEnumerable<ITableEntity> entities)
        {
            if (null == entities)
            {
                throw new ArgumentNullException("entities");
            }
            if (!entities.Any())
            {
                return null;
            }

            var result = new List<TableResult>();

            foreach (var batch in this.Batch(entities))
            {
                var batchOperation = new TableBatchOperation();
                batch.ToList().ForEach(e => batchOperation.Delete(e));
                var r = await this.reference.ExecuteBatchAsync(batchOperation);
                result.AddRange(r);
            }

            return result;
        }
        #endregion

        #region Save Data
        /// <summary>
        /// Insert or replaces the record in table
        /// </summary>
        /// <param name="entity">Entity</param>
        public virtual async Task<TableResult> InsertOrReplace(ITableEntity entity)
        {
            return await this.reference.ExecuteAsync(TableOperation.InsertOrReplace(entity));
        }

        /// <summary>
        /// Insert or replaces the record in table with sanitized table keys via the provided sanitizationProvider
        /// </summary>
        /// <remarks>This operation will set the entity PartitionKey and RowKey with sanitized values.  It will also set the entity UnsanitizedPartitionKey and UnsanitizedRowKey values.</remarks>
        /// <param name="entity">Entity</param>
        /// <param name="sanitizationProvider">The SanitizationProvider used to sanitize the keys</param>
        public virtual async Task<TableResult> InsertOrReplace(ISupportsSanitizedKeys entity, ISanitizationProvider sanitizationProvider)
        {
            if (null == entity) throw new ArgumentNullException(nameof(entity));
            if (null == sanitizationProvider) throw new ArgumentNullException(nameof(sanitizationProvider));

            this.SanitizeEntity(entity as ISupportsSanitizedKeys, sanitizationProvider);
            return await InsertOrReplace(entity);
        }

        /// <summary>
        /// Insert or replaces the record in table with sanitized table keys via the provided sanitizationProvider
        /// </summary>
        /// <remarks>This operation will set the entity PartitionKey and RowKey with the sanitized values.</remarks>
        /// <param name="entity">Entity</param>
        /// <param name="sanitizationProvider">The SanitizationProvider used to sanitize the keys</param>
        public virtual async Task<TableResult> InsertOrReplace(ITableEntity entity, ISanitizationProvider sanitizationProvider)
        {
            if (null == entity) throw new ArgumentNullException(nameof(entity));
            if (null == sanitizationProvider) throw new ArgumentNullException(nameof(sanitizationProvider));

            this.SanitizeEntity(entity, sanitizationProvider);
            return await InsertOrReplace(entity);
        }

        /// <summary>
        /// Insert batch records in table with sanitized table keys via the provided sanitizationProvider.
        /// </summary>
        /// <remarks>This operation will set the entity PartitionKey and RowKey with sanitized values.  It will also set the entity UnsanitizedPartitionKey and UnsanitizedRowKey for any ISupportsSanitizedKeys entities.</remarks>
        /// <param name="entity">Entity</param>
        /// <param name="sanitizationProvider">The SanitizationProvider used to sanitize the keys</param>
        public virtual async Task<IEnumerable<TableResult>> Insert(IEnumerable<ITableEntity> entities, ISanitizationProvider sanitizationProvider)
        {
            if (null == entities) throw new ArgumentNullException(nameof(entities));
            if (null == sanitizationProvider) throw new ArgumentNullException(nameof(sanitizationProvider));

            return await InsertInternal(entities, sanitizationProvider);
        }

        /// <summary>
        /// Insert Batch
        /// </summary>
        /// <param name="entities"></param>
        public virtual async Task<IEnumerable<TableResult>> Insert(IEnumerable<ITableEntity> entities)
        {
            if (null == entities) throw new ArgumentNullException(nameof(entities));

            return await InsertInternal(entities);
        }

        private async Task<IEnumerable<TableResult>> InsertInternal(IEnumerable<ITableEntity> entities, ISanitizationProvider sanitizationProvider = null)
        {
            if (null != sanitizationProvider)
            {
                this.SanitizeEntities(entities, sanitizationProvider);
            }

            var result = new List<TableResult>();

            foreach (var batch in this.Batch(entities))
            {
                var batchOperation = new TableBatchOperation();
                batch.ToList().ForEach(e => batchOperation.InsertOrReplace(e));
                var r = await this.reference.ExecuteBatchAsync(batchOperation);
                result.AddRange(r);
            }

            return result;
        }

        /// <summary>
        /// Insert Or Replace Entity (Dictionary) with sanitized table keys via the provided sanitizationProvider
        /// </summary>
        /// <remarks>
        /// Specify: PartitionKey, RowKey and ETag
        /// </remarks>
        /// <remarks>This operation will set the entity PartitionKey and RowKey with the sanitized values.</remarks>
        /// <param name="entity">Entity</param>
        /// <param name="sanitizationProvider">The SanitizationProvider used to sanitize the keys</param>
        public virtual async Task<TableResult> InsertOrReplace(IDictionary<string, object> entity, ISanitizationProvider sanitizationProvider)
        {
            if (null == entity) throw new ArgumentNullException(nameof(entity));
            if (null == sanitizationProvider) throw new ArgumentNullException(nameof(sanitizationProvider));

            return await this.InsertOrReplaceInternal(entity, sanitizationProvider);
        }

        /// <summary>
        /// Insert Or Replace Entity (Dictionary) 
        /// </summary>
        /// <remarks>
        /// Specify: PartitionKey, RowKey and ETag
        /// </remarks>
        /// <param name="entity">Entity</param>
        public virtual async Task<TableResult> InsertOrReplace(IDictionary<string, object> entity)
        {
            if (null == entity) throw new ArgumentNullException(nameof(entity));

            return await this.InsertOrReplaceInternal(entity);
        }

        private async Task<TableResult> InsertOrReplaceInternal(IDictionary<string, object> entity, ISanitizationProvider sanitizationProvider = null)
        {
            var properties = new Dictionary<string, EntityProperty>();
            entity.Keys.Where(k => k != PartitionKey && k != RowKey && k != ETag).ToList().ForEach(key => properties.Add(key, EntityProperty.CreateEntityPropertyFromObject(entity[key])));

            var partitionKey = entity.Keys.Contains(PartitionKey) ? entity[PartitionKey].ToString() : string.Empty;
            var rowKey = entity.Keys.Contains(RowKey) ? entity[RowKey].ToString() : string.Empty;
            var etag = entity.Keys.Contains(ETag) ? entity[ETag].ToString() : null;
            var dynamicEntity = new DynamicTableEntity(partitionKey, rowKey, etag, properties);
            if (null == sanitizationProvider)
            {
                return await this.InsertOrReplace(dynamicEntity);
            }
            else
            {
                this.SanitizeEntity(dynamicEntity, sanitizationProvider);
                return await this.InsertOrReplace(dynamicEntity, sanitizationProvider);
            }
        }

        /// <summary>
        /// Insert Batch
        /// </summary>
        /// <param name="entities">Entities</param>
        public virtual async Task<IEnumerable<TableResult>> Insert(IEnumerable<IDictionary<string, object>> entities, ISanitizationProvider sanitizationProvider)
        {
            if (null == entities) throw new ArgumentNullException(nameof(entities));
            if (null == sanitizationProvider) throw new ArgumentNullException(nameof(sanitizationProvider));

            return await this.InsertInternal(entities, sanitizationProvider);
        }
        public virtual async Task<IEnumerable<TableResult>> Insert(IEnumerable<IDictionary<string, object>> entities)
        {
            if (null == entities) throw new ArgumentNullException(nameof(entities));
            return await this.InsertInternal(entities);
        }
        private async Task<IEnumerable<TableResult>> InsertInternal(IEnumerable<IDictionary<string, object>> entities, ISanitizationProvider sanitizationProvider = null)
        {
            var result = new List<TableResult>();

            foreach (var batch in this.Batch(entities))
            {
                var batchOperation = new TableBatchOperation();

                foreach (var entity in batch)
                {
                    var properties = new Dictionary<string, EntityProperty>();
                    entity.Keys.Where(k => k != PartitionKey && k != RowKey && k != ETag).ToList().ForEach(key => properties.Add(key, EntityProperty.CreateEntityPropertyFromObject(entity[key])));

                    var partitionKey = entity.Keys.Contains(PartitionKey) ? entity[PartitionKey].ToString() : string.Empty;
                    var rowKey = entity.Keys.Contains(RowKey) ? entity[RowKey].ToString() : string.Empty;
                    var etag = entity.Keys.Contains(ETag) ? entity[ETag].ToString() : null;

                    var dynamicEntity = new DynamicTableEntity(partitionKey, rowKey, etag, properties);

                    this.SanitizeEntity(dynamicEntity, sanitizationProvider);

                    batchOperation.InsertOrMerge(dynamicEntity);
                }

                var r = await this.reference.ExecuteBatchAsync(batchOperation);
                result.AddRange(r);
            }

            return result;
        }
        #endregion

        #region Query Object
        /// <summary>
        /// Query By Partition
        /// </summary>
        /// <typeparam name="T">Return Type</typeparam>
        /// <param name="partitionKey"></param>
        /// <returns>Entities</returns>
        public virtual async Task<IEnumerable<T>> QueryByPartition<T>(string partitionKey)
            where T : ITableEntity, new()
        {
            var query = new TableQuery<T>().Where(TableQuery.GenerateFilterCondition(PartitionKey, QueryComparisons.Equal, partitionKey));
            return await this.Query<T>(query);
        }

        /// <summary>
        /// Query By Partition
        /// </summary>
        /// <remarks>
        /// Without providing the partion this query may not perform well.
        /// </remarks>
        /// <typeparam name="T">Return Type</typeparam>
        /// <param name="rowKey">Row Key</param>
        /// <returns>Entities</returns>
        public virtual async Task<IEnumerable<T>> QueryByRow<T>(string rowKey)
            where T : ITableEntity, new()
        {
            var query = new TableQuery<T>().Where(TableQuery.GenerateFilterCondition(RowKey, QueryComparisons.Equal, rowKey));
            return await this.Query<T>(query);
        }

        /// <summary>
        /// Query By Partition and Row
        /// </summary>
        /// <typeparam name="T">Return Type</typeparam>
        /// <param name="partitionKey">Partition Key</param>
        /// <param name="rowKey">Row</param>
        /// <returns></returns>
        public virtual async Task<T> QueryByPartitionAndRow<T>(string partitionKey, string rowKey)
            where T : ITableEntity, new()
        {
            var partitionFilter = TableQuery.GenerateFilterCondition(PartitionKey, QueryComparisons.Equal, partitionKey);
            var rowFilter = TableQuery.GenerateFilterCondition(RowKey, QueryComparisons.Equal, rowKey);
            var filter = TableQuery.CombineFilters(partitionFilter, TableOperators.And, rowFilter);
            var query = new TableQuery<T>().Where(filter);

            var result = await this.Query<T>(query);
            return result.FirstOrDefault();
        }

        /// <summary>
        /// Query by Expression
        /// </summary>
        /// <remarks>Filtering is done on client; can be expensive</remarks>
        /// <typeparam name="T">Return Type</typeparam>
        /// <param name="predicate">Predicate</param>
        /// <param name="maxResults">Max Result</param>
        /// <returns></returns>
        public virtual async Task<IEnumerable<T>> Query<T>(Func<T, bool> predicate, int maxResults = int.MaxValue)
            where T : ITableEntity, new()
        {
            if (null == predicate)
            {
                throw new ArgumentNullException("predicate");
            }
            if (0 >= maxResults)
            {
                throw new InvalidOperationException("maxResults: must be above 0.");
            }

            var items = await this.Query<T>(new TableQuery<T>());

            return items.Where(predicate).Take(maxResults);
        }

        /// <summary>
        /// Query
        /// </summary>
        /// <typeparam name="T">Return Type</typeparam>
        /// <param name="query">Table Query</param>
        /// <returns>Results</returns>
        public virtual async Task<IEnumerable<T>> Query<T>(TableQuery<T> query)
            where T : ITableEntity, new()
        {
            if (null == query)
            {
                throw new ArgumentNullException("query");
            }

            var entities = new List<T>();
            TableContinuationToken token = null;

            do
            {
                var queryResult = await this.reference.ExecuteQuerySegmentedAsync<T>(query, token);
                entities.AddRange(queryResult.Results);
                token = queryResult.ContinuationToken;
            }
            while (null != token);

            return entities;
        }


#if (!NETCOREAPP1_0 && !NETSTANDARD1_3)
        /// <summary>
        /// CreateQuery
        /// </summary>
        /// <typeparam name="TElement">Entity type</typeparam>
        /// <returns>IQueryable<TElement></returns>
        public IQueryable<TElement> CreateQuery<TElement>() where TElement : ITableEntity, new()
        {
            return this.reference.CreateQuery<TElement>();
        }
#endif
        #endregion

        #region Query Dictionary
        /// <summary>
        /// Query By Partition
        /// </summary>
        /// <param name="partitionKey"></param>
        /// <returns>Entities</returns>
        public virtual async Task<IEnumerable<IDictionary<string, object>>> QueryByPartition(string partitionKey)
        {
            return await this.Query(new TableQuery().Where(TableQuery.GenerateFilterCondition(TableStorage.PartitionKey, QueryComparisons.Equal, partitionKey)));
        }

        /// <summary>
        /// Query By Partition
        /// </summary>
        /// <remarks>
        /// Without providing the partion this query may not perform well.
        /// </remarks>
        /// <param name="rowKey">Row Key</param>
        /// <returns>Entities</returns>
        public virtual async Task<IEnumerable<IDictionary<string, object>>> QueryByRow(string rowKey)
        {
            return await this.Query(new TableQuery().Where(TableQuery.GenerateFilterCondition(TableStorage.RowKey, QueryComparisons.Equal, rowKey)));
        }

        /// <summary>
        /// Query By Partition and Row
        /// </summary>
        /// <param name="partitionKey">Partition Key</param>
        /// <param name="rowKey">Row</param>
        /// <returns></returns>
        public virtual async Task<IDictionary<string, object>> QueryByPartitionAndRow(string partitionKey, string rowKey)
        {
            var partitionFilter = TableQuery.GenerateFilterCondition(TableStorage.PartitionKey, QueryComparisons.Equal, partitionKey);
            var rowFilter = TableQuery.GenerateFilterCondition(TableStorage.RowKey, QueryComparisons.Equal, rowKey);
            var filter = TableQuery.CombineFilters(partitionFilter, TableOperators.And, rowFilter);
            var query = new TableQuery().Where(filter);

            var result = await this.Query(query);
            return result.FirstOrDefault();
        }

        /// <summary>
        /// Generic Query
        /// </summary>
        /// <param name="query">Query</param>
        /// <returns>Entities</returns>
        public virtual async Task<IEnumerable<IDictionary<string, object>>> Query(TableQuery query)
        {
            if (null == query)
            {
                throw new ArgumentNullException("query");
            }

            var q = new TableQuery<DynamicTableEntity>()
            {
                FilterString = query.FilterString,
                SelectColumns = query.SelectColumns,
                TakeCount = query.TakeCount
            };

            var entities = new List<DynamicTableEntity>();
            TableContinuationToken token = null;

            do
            {
                var queryResult = await this.reference.ExecuteQuerySegmentedAsync<DynamicTableEntity>(q, token);
                entities.AddRange(queryResult.Results);
                token = queryResult.ContinuationToken;
            }
            while (null != token);

            var results = new List<IDictionary<string, object>>();
            foreach (var e in entities)
            {
                var dic = new Dictionary<string, object>();
                foreach (var p in e.Properties)
                {
                    dic.Add(p.Key, p.Value.PropertyAsObject);
                }
                dic.Add(TableStorage.PartitionKey, e.PartitionKey);
                dic.Add(TableStorage.RowKey, e.RowKey);
                dic.Add(TableStorage.ETag, e.ETag);
                dic.Add(TableStorage.Timestamp, e.Timestamp.DateTime);
                results.Add(dic);
            }

            return results;
        }
        #endregion

        #region Additional Methods
        /// <summary>
        /// Break Entities into batches
        /// </summary>
        /// <param name="entities">Entities</param>
        /// <returns>Batches</returns>
        public virtual IEnumerable<IEnumerable<ITableEntity>> Batch(IEnumerable<ITableEntity> entities)
        {
            return entities.GroupBy(en => en.PartitionKey).SelectMany(e => this.Chunk<ITableEntity>(e));
        }

        /// <summary>
        /// Break Entities into batches
        /// </summary>
        /// <param name="entities">Entities</param>
        /// <returns>Batches</returns>
        public virtual IEnumerable<IEnumerable<IDictionary<string, object>>> Batch(IEnumerable<IDictionary<string, object>> entities)
        {
            return entities.GroupBy(en => en[PartitionKey]).SelectMany(e => this.Chunk<IDictionary<string, object>>(e));
        }

        /// <summary>
        /// Chunk data into smaller blocks
        /// </summary>
        /// <typeparam name="T">Type</typeparam>
        /// <param name="entities">Entities</param>
        /// <returns>Chunks</returns>
        public virtual IEnumerable<IEnumerable<T>> Chunk<T>(IEnumerable<T> entities)
        {
            return entities.Select((x, i) => new { Index = i, Value = x }).GroupBy(x => x.Index / TableStorage.MaimumxInsertBatch).Select(x => x.Select(v => v.Value));
        }
        #endregion

        #region Sanitization
        private void SanitizeEntities(IEnumerable<ITableEntity> entities, ISanitizationProvider sanitizationProvider)
        {
            if (sanitizationProvider == null) throw new ArgumentNullException(nameof(sanitizationProvider));

            foreach (ITableEntity entity in entities)
            {
                var entitySupportsSanitizedKeys = entity as ISupportsSanitizedKeys;
                if (entitySupportsSanitizedKeys != null)
                {
                    SanitizeEntity(entitySupportsSanitizedKeys, sanitizationProvider);
                }
                else
                {
                    SanitizeEntity(entity, sanitizationProvider);
                }
            }
        }
        private void SanitizeEntity(ISupportsSanitizedKeys entity, ISanitizationProvider sanitizationProvider)
        {
            entity.SanitizeKeys(sanitizationProvider);
        }
        private void SanitizeEntity(ITableEntity entity, ISanitizationProvider sanitizationProvider)
        {
            if (sanitizationProvider == null) return;
            entity.PartitionKey = sanitizationProvider.Sanitize(entity.PartitionKey);
            entity.RowKey = sanitizationProvider.Sanitize(entity.RowKey);
        }
        #endregion
    }
}