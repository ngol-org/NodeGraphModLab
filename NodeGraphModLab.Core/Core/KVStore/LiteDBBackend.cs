using LiteDB;

namespace NodeGraphModLab.Core.KVStore;

internal sealed class LiteDBBackend : IKVStoreBackend
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<BsonDocument> _col;

    public LiteDBBackend(string dbPath)
    {
        _db  = new LiteDatabase(dbPath);
        _col = _db.GetCollection("kv");
    }

    public IEnumerable<(string, string)> LoadAll()
        => _col.FindAll().Select(d => (d["_id"].AsString, d["value"].AsString));

    public void Upsert(string key, string valueJson)
    {
        // _id = key の BsonDocument として upsert — LiteDB の _id unique を直接利用
        var doc = new BsonDocument
        {
            ["_id"]   = new BsonValue(key),
            ["value"] = new BsonValue(valueJson)
        };
        _col.Upsert(doc);
    }

    public void Delete(string key)
        => _col.Delete(new BsonValue(key));

    public void Dispose() => _db.Dispose();
}
