using System;
using System.Text.RegularExpressions;
using HotChocolate.Data.Filters;
using HotChocolate.Language;
using MongoDB.Bson;
using MongoDB.Driver;

namespace HotChocolate.MongoDb.Data.Filters
{
    public class MongoDbStringContainsHandler
        : MongoDbStringOperationHandler
    {
        public MongoDbStringContainsHandler()
        {
            CanBeNull = false;
        }

        protected override int Operation => DefaultOperations.Contains;

        public override FilterDefinition<BsonDocument> HandleOperation(
            MongoDbFilterVisitorContext context,
            IFilterOperationField field,
            IValueNode value,
            object? parsedValue)
        {
            if (parsedValue is string str)
            {
                var doc = new BsonDocument(
                    "$regex",
                    new BsonRegularExpression($"/{Regex.Escape(str)}/"));

                return new BsonDocument(
                    context.GetMongoFilterScope().GetPath(),
                    doc);
            }

            throw new InvalidOperationException();
        }
    }
}
