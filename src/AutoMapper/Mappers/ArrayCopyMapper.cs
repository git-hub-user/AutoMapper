using System;
using System.Linq.Expressions;
using System.Reflection;
using AutoMapper.Execution;
using AutoMapper.Internal;

namespace AutoMapper.Mappers
{
    using static Expression;

    public class ArrayCopyMapper : ArrayMapper
    {
        private static readonly MethodInfo ArrayCopyMethod = typeof(Array).GetMethod("Copy", new[] { typeof(Array), typeof(Array), typeof(long) });
        private static readonly PropertyInfo ArrayLengthProperty = typeof(Array).GetProperty("LongLength");

        public override bool IsMatch(in TypePair context) =>
            context.DestinationType.IsArray
            && context.SourceType.IsArray
            && ElementTypeHelper.GetElementType(context.DestinationType) == ElementTypeHelper.GetElementType(context.SourceType)
            && ElementTypeHelper.GetElementType(context.SourceType).IsPrimitive;

        public override Expression MapExpression(IGlobalConfiguration configurationProvider, ProfileMap profileMap,
            IMemberMap memberMap, Expression sourceExpression, Expression destExpression, Expression contextExpression)
        {
            var destElementType = ElementTypeHelper.GetElementType(destExpression.Type);
            var sourceElementType = ElementTypeHelper.GetElementType(sourceExpression.Type);

            if (configurationProvider.FindTypeMapFor(sourceElementType, destElementType) != null)
                return base.MapExpression(configurationProvider, profileMap, memberMap, sourceExpression, destExpression, contextExpression);

            var valueIfNullExpr = profileMap.AllowsNullCollectionsFor(memberMap) ? 
                (Expression) Constant(null, destExpression.Type) : 
                NewArrayBounds(destElementType, Constant(0));

            var dest = Parameter(destExpression.Type, "destArray");
            var sourceLength = Parameter(ArrayLengthProperty.PropertyType, "sourceLength");
            var mapExpr = Block(
                new[] {dest, sourceLength},
                Assign(sourceLength, Property(sourceExpression, ArrayLengthProperty)),
                Assign(dest, NewArrayBounds(destElementType, sourceLength)),
                Call(ArrayCopyMethod, sourceExpression, dest, sourceLength),
                dest
            );

            return Condition(Equal(sourceExpression, ExpressionFactory.Null), valueIfNullExpr, mapExpr);

        }
    }
}
