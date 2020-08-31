using System.Linq.Expressions;
using HotChocolate.Language;
using HotChocolate.Types;

namespace HotChocolate.Data.Filters.Expressions
{
    public class QueryableBooleanEqualsHandler
       : QueryableBooleanOperationHandler
    {
        protected override int Operation => DefaultOperations.Equals;

        public override Expression HandleOperation(
            QueryableFilterContext context,
            IFilterOperationField field,
            IValueNode value,
            object parsedValue)
        {
            Expression property = context.GetInstance();
            return FilterExpressionBuilder.Equals(property, parsedValue);
        }
    }
}
