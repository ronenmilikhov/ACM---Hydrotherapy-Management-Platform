using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Google.Cloud.Firestore;

namespace Amazon
{
    public class RegionEndpoint
    {
        public static object EUNorth1 = new object();
    }
}

namespace Amazon.DynamoDBv2.DocumentModel
{
    public class Document { }
}

namespace Amazon.DynamoDBv2.Model
{
    public class AttributeValue
    {
        public string? S { get; set; }
        public string? N { get; set; }
        public bool? BOOL { get; set; }
        public bool? NULL { get; set; }
        public List<AttributeValue>? L { get; set; }
        public Dictionary<string, AttributeValue>? M { get; set; }
        public List<string>? SS { get; set; }
        public List<string>? NS { get; set; }
    }

    public class AttributeValueUpdate
    {
        public AttributeValue? Value { get; set; }
        public AttributeAction? Action { get; set; }
    }

    public enum AttributeAction
    {
        ADD,
        PUT,
        DELETE
    }

    public class GetItemRequest
    {
        public string TableName { get; set; } = string.Empty;
        public Dictionary<string, AttributeValue> Key { get; set; } = new();
    }

    public class GetItemResponse
    {
        public Dictionary<string, AttributeValue>? Item { get; set; }
    }

    public class PutItemRequest
    {
        public string TableName { get; set; } = string.Empty;
        public Dictionary<string, AttributeValue> Item { get; set; } = new();
    }

    public class PutItemResponse
    {
    }

    public class UpdateItemRequest
    {
        public string TableName { get; set; } = string.Empty;
        public Dictionary<string, AttributeValue> Key { get; set; } = new();
        public Dictionary<string, AttributeValueUpdate> AttributeUpdates { get; set; } = new();
    }

    public class UpdateItemResponse
    {
    }

    public class DeleteItemRequest
    {
        public string TableName { get; set; } = string.Empty;
        public Dictionary<string, AttributeValue> Key { get; set; } = new();
    }

    public class DeleteItemResponse
    {
    }

    public class ScanRequest
    {
        public string TableName { get; set; } = string.Empty;
        public string? FilterExpression { get; set; }
        public string? ProjectionExpression { get; set; }
        public Dictionary<string, string>? ExpressionAttributeNames { get; set; }
        public Dictionary<string, AttributeValue>? ExpressionAttributeValues { get; set; }
        public int? Limit { get; set; }
    }

    public class ScanResponse
    {
        public List<Dictionary<string, AttributeValue>> Items { get; set; } = new();
        public int Count => Items.Count;
    }

    public class QueryRequest
    {
        public string TableName { get; set; } = string.Empty;
        public string? KeyConditionExpression { get; set; }
        public string? ProjectionExpression { get; set; }
        public Dictionary<string, string>? ExpressionAttributeNames { get; set; }
        public Dictionary<string, AttributeValue>? ExpressionAttributeValues { get; set; }
        public bool ScanIndexForward { get; set; } = true;
        public int? Limit { get; set; }
    }

    public class QueryResponse
    {
        public List<Dictionary<string, AttributeValue>> Items { get; set; } = new();
    }
}

namespace Amazon.DynamoDBv2
{
    using Amazon.DynamoDBv2.Model;

    public class AmazonDynamoDBClient : IDisposable
    {
        private readonly FirestoreDb _firestoreDb;

        private static string GetProjectId()
        {
            return Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ?? "acm-application-38298";
        }

        public AmazonDynamoDBClient()
        {
            _firestoreDb = FirestoreDb.Create(GetProjectId());
        }

        public AmazonDynamoDBClient(object region)
        {
            _firestoreDb = FirestoreDb.Create(GetProjectId());
        }

        public async Task<GetItemResponse> GetItemAsync(string tableName, Dictionary<string, AttributeValue> key)
        {
            return await GetItemAsync(new GetItemRequest { TableName = tableName, Key = key });
        }

        public async Task<GetItemResponse> GetItemAsync(GetItemRequest request)
        {
            if (request.Key == null || request.Key.Count == 0)
                return new GetItemResponse { Item = null };

            string docIdStr = GetDocIdFromKey(request.Key, request.TableName);
            DocumentReference docRef = _firestoreDb.Collection(request.TableName).Document(docIdStr);
            DocumentSnapshot doc = await docRef.GetSnapshotAsync();

            if (!doc.Exists)
                return new GetItemResponse { Item = null };

            var dict = doc.ToDictionary();
            var attrDict = ConvertToAttributeValues(dict);

            EnsureKeyFieldsInItem(request.TableName, doc.Id, attrDict);

            return new GetItemResponse { Item = attrDict };
        }

        public async Task<PutItemResponse> PutItemAsync(string tableName, Dictionary<string, AttributeValue> item)
        {
            return await PutItemAsync(new PutItemRequest { TableName = tableName, Item = item });
        }

        public async Task<PutItemResponse> PutItemAsync(PutItemRequest request)
        {
            string docIdStr = GetDocIdFromItem(request.Item, request.TableName);
            var dict = ConvertFromAttributeValues(request.Item);

            DocumentReference docRef = _firestoreDb.Collection(request.TableName).Document(docIdStr);
            await docRef.SetAsync(dict, SetOptions.Overwrite);

            return new PutItemResponse();
        }

        public async Task<UpdateItemResponse> UpdateItemAsync(string tableName, Dictionary<string, AttributeValue> key, Dictionary<string, AttributeValueUpdate> updates)
        {
            return await UpdateItemAsync(new UpdateItemRequest { TableName = tableName, Key = key, AttributeUpdates = updates });
        }

        public async Task<UpdateItemResponse> UpdateItemAsync(UpdateItemRequest request)
        {
            string docIdStr = GetDocIdFromKey(request.Key, request.TableName);
            DocumentReference docRef = _firestoreDb.Collection(request.TableName).Document(docIdStr);

            var updates = new Dictionary<string, object?>();
            foreach (var update in request.AttributeUpdates)
            {
                if (update.Value.Action == AttributeAction.PUT)
                {
                    updates[update.Key] = ConvertFromAttributeValue(update.Value.Value);
                }
                else if (update.Value.Action == AttributeAction.DELETE)
                {
                    updates[update.Key] = FieldValue.Delete;
                }
            }

            if (updates.Count > 0)
            {
                await docRef.UpdateAsync(updates);
            }

            return new UpdateItemResponse();
        }

        public async Task<DeleteItemResponse> DeleteItemAsync(string tableName, Dictionary<string, AttributeValue> key)
        {
            return await DeleteItemAsync(new DeleteItemRequest { TableName = tableName, Key = key });
        }

        public async Task<DeleteItemResponse> DeleteItemAsync(DeleteItemRequest request)
        {
            string docIdStr = GetDocIdFromKey(request.Key, request.TableName);
            DocumentReference docRef = _firestoreDb.Collection(request.TableName).Document(docIdStr);
            await docRef.DeleteAsync();
            return new DeleteItemResponse();
        }

        private static bool CompareAttributeValues(AttributeValue a, AttributeValue b, string op)
        {
            string? valA = a.S ?? a.N ?? a.BOOL?.ToString();
            string? valB = b.S ?? b.N ?? b.BOOL?.ToString();

            if (valA == null || valB == null) return false;

            if (double.TryParse(valA, out double numA) && double.TryParse(valB, out double numB))
            {
                if (op == "=") return numA == numB;
                if (op == "<>") return numA != numB;
                if (op == ">") return numA > numB;
                if (op == "<") return numA < numB;
                if (op == ">=") return numA >= numB;
                if (op == "<=") return numA <= numB;
            }

            int cmp = string.Compare(valA, valB, StringComparison.OrdinalIgnoreCase);
            if (op == "=") return cmp == 0;
            if (op == "<>") return cmp != 0;
            if (op == ">") return cmp > 0;
            if (op == "<") return cmp < 0;
            if (op == ">=") return cmp >= 0;
            if (op == "<=") return cmp <= 0;

            return false;
        }

        public async Task<ScanResponse> ScanAsync(ScanRequest request)
        {
            Query query = _firestoreDb.Collection(request.TableName);
            QuerySnapshot snapshot = await query.GetSnapshotAsync();
            var items = new List<Dictionary<string, AttributeValue>>();
            foreach (DocumentSnapshot doc in snapshot.Documents)
            {
                if (doc.Exists)
                {
                    var dict = doc.ToDictionary();
                    var attrDict = ConvertToAttributeValues(dict);
                    EnsureKeyFieldsInItem(request.TableName, doc.Id, attrDict);
                    items.Add(attrDict);
                }
            }

            if (!string.IsNullOrWhiteSpace(request.FilterExpression))
            {
                string filterExpr = request.FilterExpression;
                if (request.ExpressionAttributeNames != null)
                {
                    foreach (var nameMap in request.ExpressionAttributeNames)
                    {
                        filterExpr = filterExpr.Replace(nameMap.Key, nameMap.Value);
                    }
                }

                string[] parts = Regex.Split(filterExpr, @"\s+AND\s+", RegexOptions.IgnoreCase);
                var filteredItems = new List<Dictionary<string, AttributeValue>>();

                foreach (var item in items)
                {
                    bool matchesAll = true;
                    foreach (var part in parts)
                    {
                        var trimmedPart = part.Trim();
                        if (string.IsNullOrEmpty(trimmedPart)) continue;

                        string? op = null;
                        string[] opCandidates = new[] { "<=", ">=", "<>", "=", ">", "<" };
                        foreach (var candidate in opCandidates)
                        {
                            if (trimmedPart.Contains(candidate))
                            {
                                op = candidate;
                                break;
                            }
                        }

                        if (op == null) continue;

                        int opIdx = trimmedPart.IndexOf(op);
                        string field = trimmedPart.Substring(0, opIdx).Trim();
                        string valPlaceholder = trimmedPart.Substring(opIdx + op.Length).Trim();

                        if (request.ExpressionAttributeValues != null && request.ExpressionAttributeValues.TryGetValue(valPlaceholder, out var attrVal))
                        {
                            if (!item.TryGetValue(field, out var itemAttrVal))
                            {
                                matchesAll = false;
                                break;
                            }

                            if (!CompareAttributeValues(itemAttrVal, attrVal, op))
                            {
                                matchesAll = false;
                                break;
                            }
                        }
                        else
                        {
                            matchesAll = false;
                            break;
                        }
                    }

                    if (matchesAll)
                    {
                        filteredItems.Add(item);
                    }
                }

                items = filteredItems;
            }

            if (request.Limit.HasValue)
            {
                items = items.Take(request.Limit.Value).ToList();
            }

            return new ScanResponse { Items = items };
        }

        public async Task<QueryResponse> QueryAsync(QueryRequest request)
        {
            Query query = _firestoreDb.Collection(request.TableName);

            if (!string.IsNullOrWhiteSpace(request.KeyConditionExpression))
            {
                string expr = request.KeyConditionExpression;
                if (request.ExpressionAttributeNames != null)
                {
                    foreach (var nameMap in request.ExpressionAttributeNames)
                    {
                        expr = expr.Replace(nameMap.Key, nameMap.Value);
                    }
                }

                string[] parts = Regex.Split(expr, @"\s+AND\s+", RegexOptions.IgnoreCase);

                foreach (var part in parts)
                {
                    var trimmedPart = part.Trim();
                    if (string.IsNullOrEmpty(trimmedPart)) continue;

                    string? op = null;
                    string[] opCandidates = new[] { "<=", ">=", "<>", "=", ">", "<" };
                    foreach (var candidate in opCandidates)
                    {
                        if (trimmedPart.Contains(candidate))
                        {
                            op = candidate;
                            break;
                        }
                    }

                    if (op == null) continue;

                    int opIdx = trimmedPart.IndexOf(op);
                    string field = trimmedPart.Substring(0, opIdx).Trim();
                    string valPlaceholder = trimmedPart.Substring(opIdx + op.Length).Trim();

                    if (request.ExpressionAttributeValues != null && request.ExpressionAttributeValues.TryGetValue(valPlaceholder, out var attrVal))
                    {
                        object? val = ConvertFromAttributeValue(attrVal);
                        if (val != null)
                        {
                            if (op == "=")
                                query = query.WhereEqualTo(field, val);
                            else if (op == "<>")
                                query = query.WhereNotEqualTo(field, val);
                            else if (op == ">")
                                query = query.WhereGreaterThan(field, val);
                            else if (op == "<")
                                query = query.WhereLessThan(field, val);
                            else if (op == ">=")
                                query = query.WhereGreaterThanOrEqualTo(field, val);
                            else if (op == "<=")
                                query = query.WhereLessThanOrEqualTo(field, val);
                        }
                    }
                }
            }

            QuerySnapshot snapshot = await query.GetSnapshotAsync();
            var items = new List<Dictionary<string, AttributeValue>>();
            foreach (DocumentSnapshot doc in snapshot.Documents)
            {
                if (doc.Exists)
                {
                    var dict = doc.ToDictionary();
                    var attrDict = ConvertToAttributeValues(dict);
                    EnsureKeyFieldsInItem(request.TableName, doc.Id, attrDict);
                    items.Add(attrDict);
                }
            }

            // In-memory sort to avoid requiring composite indexes for simple queries
            items = items.OrderBy(item =>
            {
                if (item.TryGetValue("Id", out var idVal) && idVal.N != null && int.TryParse(idVal.N, out int numericId))
                {
                    return numericId;
                }
                return 0;
            }).ToList();

            if (!request.ScanIndexForward)
            {
                items.Reverse();
            }

            if (request.Limit.HasValue && items.Count > request.Limit.Value)
            {
                items = items.Take(request.Limit.Value).ToList();
            }

            return new QueryResponse { Items = items };
        }

        private string GetDocIdFromKey(Dictionary<string, AttributeValue> key, string tableName)
        {
            if (tableName == "GroupChildren")
            {
                string? groupId = null;
                string? childId = null;
                if (key.TryGetValue("GroupId", out var gVal)) groupId = gVal.N ?? gVal.S;
                if (key.TryGetValue("ChildId", out var cVal)) childId = cVal.N ?? cVal.S;
                if (groupId != null && childId != null)
                {
                    return $"{groupId.Trim()}_{childId.Trim()}";
                }
            }
            if (key.TryGetValue("Id", out var idVal))
            {
                return (idVal.N ?? idVal.S ?? "").Trim();
            }
            if (key.TryGetValue("Email", out var emailVal))
            {
                return (emailVal.S ?? "").Trim();
            }
            var firstVal = key.Values.First();
            return (firstVal.S ?? firstVal.N ?? firstVal.BOOL?.ToString() ?? "").Trim();
        }

        private string GetDocIdFromItem(Dictionary<string, AttributeValue> item, string tableName)
        {
            if (tableName == "GroupChildren")
            {
                string? groupId = null;
                string? childId = null;
                if (item.TryGetValue("GroupId", out var gVal)) groupId = gVal.N ?? gVal.S;
                if (item.TryGetValue("ChildId", out var cVal)) childId = cVal.N ?? cVal.S;
                if (groupId != null && childId != null)
                {
                    return $"{groupId.Trim()}_{childId.Trim()}";
                }
            }

            string? docIdStr = null;

            if (item.TryGetValue("Email", out var emailVal))
            {
                docIdStr = emailVal.S;
            }
            else if (item.TryGetValue("ExpoPushToken", out var tokenVal))
            {
                docIdStr = tokenVal.S;
            }
            else if (item.TryGetValue("Id", out var idVal))
            {
                docIdStr = idVal.N ?? idVal.S;
            }
            else
            {
                foreach (var pair in item)
                {
                    if (pair.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
                    {
                        docIdStr = pair.Value.N ?? pair.Value.S;
                        if (docIdStr != null) break;
                    }
                }
            }

            docIdStr = docIdStr?.Trim();
            return string.IsNullOrEmpty(docIdStr) ? Guid.NewGuid().ToString() : docIdStr;
        }

        private void EnsureKeyFieldsInItem(string tableName, string docId, Dictionary<string, AttributeValue> item)
        {
            if (tableName == "Parents" || tableName == "Instructors")
            {
                if (!item.ContainsKey("Email"))
                {
                    item["Email"] = new AttributeValue { S = docId };
                }
            }
            else if (tableName == "PushDeviceTokens")
            {
                if (!item.ContainsKey("ExpoPushToken"))
                {
                    item["ExpoPushToken"] = new AttributeValue { S = docId };
                }
            }
            else
            {
                if (!item.ContainsKey("Id"))
                {
                    if (int.TryParse(docId, out int numericId))
                    {
                        item["Id"] = new AttributeValue { N = docId };
                    }
                    else
                    {
                        item["Id"] = new AttributeValue { S = docId };
                    }
                }
            }
        }

        public static object? ConvertFromAttributeValue(AttributeValue? attr)
        {
            if (attr == null) return null;
            if (attr.S != null) return attr.S;
            if (attr.N != null)
            {
                if (long.TryParse(attr.N, out long lVal))
                {
                    if (lVal >= int.MinValue && lVal <= int.MaxValue)
                        return (int)lVal;
                    return lVal;
                }
                if (double.TryParse(attr.N, out double dVal)) return dVal;
                return attr.N;
            }
            if (attr.BOOL != null) return attr.BOOL.Value;
            if (attr.L != null)
            {
                var list = new List<object?>();
                foreach (var item in attr.L)
                {
                    list.Add(ConvertFromAttributeValue(item));
                }
                return list;
            }
            if (attr.M != null)
            {
                var map = new Dictionary<string, object?>();
                foreach (var pair in attr.M)
                {
                    map[pair.Key] = ConvertFromAttributeValue(pair.Value);
                }
                return map;
            }
            if (attr.SS != null) return attr.SS;
            if (attr.NS != null) return attr.NS;
            return null;
        }

        public static Dictionary<string, object?> ConvertFromAttributeValues(Dictionary<string, AttributeValue> item)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var pair in item)
            {
                dict[pair.Key] = ConvertFromAttributeValue(pair.Value);
            }
            return dict;
        }

        public static AttributeValue ConvertToAttributeValue(object? val)
        {
            if (val == null) return new AttributeValue();
            if (val is string s) return new AttributeValue { S = s };
            if (val is bool b) return new AttributeValue { BOOL = b };
            if (val is int i) return new AttributeValue { N = i.ToString() };
            if (val is long lVal) return new AttributeValue { N = lVal.ToString() };
            if (val is double d) return new AttributeValue { N = d.ToString(CultureInfo.InvariantCulture) };
            if (val is float f) return new AttributeValue { N = f.ToString(CultureInfo.InvariantCulture) };
            if (val is decimal dec) return new AttributeValue { N = dec.ToString(CultureInfo.InvariantCulture) };
            
            if (val is IDictionary<string, object> dict)
            {
                var m = new Dictionary<string, AttributeValue>();
                foreach (var pair in dict)
                {
                    m[pair.Key] = ConvertToAttributeValue(pair.Value);
                }
                return new AttributeValue { M = m };
            }
            if (val is IEnumerable<object> list)
            {
                var resultList = new List<AttributeValue>();
                foreach (var item in list)
                {
                    resultList.Add(ConvertToAttributeValue(item));
                }
                return new AttributeValue { L = resultList };
            }
            if (val is IEnumerable<string> ss)
            {
                return new AttributeValue { SS = ss.ToList() };
            }

            return new AttributeValue { S = val.ToString() };
        }

        public static Dictionary<string, AttributeValue> ConvertToAttributeValues(IDictionary<string, object> dict)
        {
            var item = new Dictionary<string, AttributeValue>();
            foreach (var pair in dict)
            {
                item[pair.Key] = ConvertToAttributeValue(pair.Value);
            }
            return item;
        }

        public void Dispose()
        {
        }
    }
}
