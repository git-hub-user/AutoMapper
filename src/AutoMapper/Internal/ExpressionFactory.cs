using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace AutoMapper.Internal
{
    using static Expression;
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ExpressionFactory
    {
        public static readonly MethodInfo ObjectToString = typeof(object).GetMethod("ToString");
        private static readonly MethodInfo DisposeMethod = typeof(IDisposable).GetMethod("Dispose");
        public static readonly Expression False = Constant(false);
        public static readonly Expression True = Constant(true);
        public static readonly Expression Null = Constant(null);
        public static readonly Expression Empty = Empty();
        public static bool IsQuery(this Expression expression) => expression is MethodCallExpression { Method: var method } && method.IsStatic && method.DeclaringType == typeof(Enumerable);
        public static Expression Chain(this IEnumerable<Expression> expressions, Expression parameter) => expressions.Aggregate(parameter,
            (left, right) => right is LambdaExpression lambda ? lambda.ReplaceParameters(left) : right.Replace(right.GetChain().FirstOrDefault().Target, left));
        public static LambdaExpression Lambda(this MemberInfo member) => new[] { member }.Lambda();
        public static LambdaExpression Lambda(this IReadOnlyCollection<MemberInfo> members)
        {
            var source = Parameter(members.First().DeclaringType, "source");
            return Expression.Lambda(members.Chain(source), source);
        }
        public static Expression Chain(this IEnumerable<MemberInfo> members, Expression obj) =>
            members.Aggregate(obj,
                        (target, getter) => getter is MethodInfo method ?
                            (Expression)(method.IsStatic ? Call(null, method, target) : Call(target, method)) :
                            MakeMemberAccess(getter.IsStatic() ? null : target, getter));
        public static IEnumerable<MemberInfo> GetMembersChain(this LambdaExpression lambda) => lambda.Body.GetMembersChain();
        public static MemberInfo GetMember(this LambdaExpression lambda) =>
            (lambda?.Body is MemberExpression memberExpression && memberExpression.Expression == lambda.Parameters[0]) ? memberExpression.Member : null;
        public static IEnumerable<MemberInfo> GetMembersChain(this Expression expression) => expression.GetChain().Select(m => m.MemberInfo);
        public static Stack<Member> GetChain(this Expression expression)
        {
            var stack = new Stack<Member>();
            while (expression != null)
            {
                var member = expression switch
                {
                    MemberExpression { Expression: var target, Member: var propertyOrField } when !propertyOrField.IsStatic() => 
                        new Member(expression, propertyOrField, target),
                    MethodCallExpression { Method: var extensionMethod, Arguments: var arguments } when arguments.Count > 0 && extensionMethod.IsExtensionMethod() =>
                        new Member(expression, extensionMethod, arguments[0]),
                    MethodCallExpression { Method: var instanceMethod, Object: var target } when !instanceMethod.IsStatic => 
                        new Member(expression, instanceMethod, target),
                    _ => default
                };
                if (member.Expression == null)
                {
                    break;
                }
                stack.Push(member);
                expression = member.Target;
            }
            return stack;
        }
        public readonly struct Member
        {
            public Member(Expression expression, MemberInfo memberInfo, Expression target)
            {
                Expression = expression;
                MemberInfo = memberInfo;
                Target = target;
            }
            public Expression Expression { get; }
            public MemberInfo MemberInfo { get; }
            public Expression Target { get; }
        }
        public static IEnumerable<MemberExpression> GetMemberExpressions(this Expression expression)
        {
            var memberExpression = expression as MemberExpression;
            if (memberExpression == null)
            {
                return Array.Empty<MemberExpression>();
            }
            return expression.GetChain().Select(m => m.Expression as MemberExpression).TakeWhile(m => m != null);
        }
        public static void EnsureMemberPath(this LambdaExpression exp, string name)
        {
            if (!exp.IsMemberPath())
            {
                throw new ArgumentOutOfRangeException(name, "Only member accesses are allowed. " + exp);
            }
        }
        public static bool IsMemberPath(this LambdaExpression lambda)
        {
            Expression currentExpression = null;
            foreach (var member in lambda.Body.GetChain())
            {
                currentExpression = member.Expression;
                if (!(currentExpression is MemberExpression))
                {
                    return false;
                }
            }
            return currentExpression == lambda.Body;
        }
        public static LambdaExpression MemberAccessLambda(Type type, string memberPath) =>
            ReflectionHelper.GetMemberPath(type, memberPath).Lambda();
        public static Expression GetSetter(MemberExpression memberExpression)
        {
            var propertyOrField = memberExpression.Member;
            return ReflectionHelper.CanBeSet(propertyOrField) ?
                        MakeMemberAccess(memberExpression.Expression, propertyOrField) :
                        null;
        }
        public static MethodInfo Method<T>(Expression<Func<T>> expression) => ((MethodCallExpression)expression.Body).Method;
        public static Expression ForEach(Expression collection, ParameterExpression loopVar, Expression loopContent)
        {
            if (collection.Type.IsArray)
            {
                return ForEachArrayItem(collection, arrayItem => Block(new[] { loopVar }, Assign(loopVar, arrayItem), loopContent));
            }
            var getEnumerator = collection.Type.GetInheritedMethod("GetEnumerator");
            var getEnumeratorCall = Call(collection, getEnumerator);
            var enumeratorType = getEnumeratorCall.Type;
            var enumeratorVar = Variable(enumeratorType, "enumerator");
            var enumeratorAssign = Assign(enumeratorVar, getEnumeratorCall);

            var moveNext = enumeratorType.GetInheritedMethod("MoveNext");
            var moveNextCall = Call(enumeratorVar, moveNext);

            var breakLabel = Label("LoopBreak");

            var loop = Block(new[] { enumeratorVar },
                enumeratorAssign,
                Using(enumeratorVar,
                    Loop(
                        IfThenElse(
                            Equal(moveNextCall, True),
                            Block(new[] { loopVar },
                                Assign(loopVar, ToType(Property(enumeratorVar, "Current"), loopVar.Type)),
                                loopContent
                            ),
                            Break(breakLabel)
                        ),
                    breakLabel))
            );

            return loop;
        }
        public static Expression ForEachArrayItem(Expression array, Func<Expression, Expression> body)
        {
            var length = Property(array, "Length");
            return For(length, index => body(ArrayAccess(array, index)));
        }
        public static Expression For(Expression count, Func<Expression, Expression> body)
        {
            var breakLabel = Label("LoopBreak");
            var index = Variable(typeof(int), "sourceArrayIndex");
            var initialize = Assign(index, Constant(0, typeof(int)));
            var loop = Block(new[] { index },
                initialize,
                Loop(
                    IfThenElse(
                        LessThan(index, count),
                        Block(body(index), PostIncrementAssign(index)),
                        Break(breakLabel)
                    ),
                breakLabel)
            );
            return loop;
        }
        public static Expression ToObject(this Expression expression) => ToType(expression, typeof(object));
        public static Expression ToType(Expression expression, Type type) => expression.Type == type ? expression : Convert(expression, type);
        public static Expression ReplaceParameters(this LambdaExpression initialLambda, params Expression[] newParameters) =>
            new ReplaceExpressionVisitor().Replace(initialLambda, newParameters);
        public static Expression ConvertReplaceParameters(this LambdaExpression initialLambda, params Expression[] newParameters) =>
            new ConvertingVisitor().Replace(initialLambda, newParameters);
        private static Expression Replace(this ReplaceVisitor visitor, LambdaExpression initialLambda, params Expression[] newParameters)
        {
            var newLambda = initialLambda.Body;
            for (var i = 0; i < Math.Min(newParameters.Length, initialLambda.Parameters.Count); i++)
            {
                visitor.OldNode = initialLambda.Parameters[i];
                visitor.NewNode = newParameters[i];
                newLambda = visitor.Visit(newLambda);
            }
            return newLambda;
        }
        public static Expression Replace(this Expression exp, Expression old, Expression replace) => 
            new ReplaceExpressionVisitor { OldNode = old, NewNode = replace }.Visit(exp);
        public static LambdaExpression Concat(LambdaExpression expr, LambdaExpression concat) => 
            (LambdaExpression)new ExpressionConcatVisitor(expr).Visit(concat);
        public static Expression NullCheck(this Expression expression, Type destinationType = null)
        {
            destinationType ??= expression.Type;
            var chain = expression.GetChain();
            if (chain.Count == 0 || chain.Peek().Target is not ParameterExpression parameter)
            {
                return expression;
            }
            var variables = new List<ParameterExpression> { parameter };
            var nullCheck = False;
            var name = parameter.Name;
            foreach (var member in chain)
            {
                var variable = Variable(member.Target.Type, name);
                name += member.MemberInfo.Name;
                var assignment = Assign(variable, UpdateTarget(member.Target, variables[variables.Count - 1]));
                variables.Add(variable);
                var nullCheckVariable = variable.Type.IsValueType ? (Expression)Block(assignment, False) : Equal(assignment, Constant(null, variable.Type));
                nullCheck = OrElse(nullCheck, nullCheckVariable);
            }
            var returnType = Nullable.GetUnderlyingType(destinationType) == expression.Type ? destinationType : expression.Type;
            var nonNullExpression = UpdateTarget(expression, variables[variables.Count - 1]);
            return Block(variables.Skip(1), Condition(nullCheck, Default(returnType), ToType(nonNullExpression, returnType)));
            static Expression UpdateTarget(Expression sourceExpression, Expression newTarget) =>
                sourceExpression switch
                {
                    MethodCallExpression methodCall when methodCall.Method.IsStatic => methodCall.Update(null, new[] { newTarget }.Concat(methodCall.Arguments.Skip(1))),
                    MethodCallExpression methodCall => methodCall.Update(newTarget, methodCall.Arguments),
                    MemberExpression memberExpression => memberExpression.Update(newTarget),
                    _ => sourceExpression,
                };
        }
        public static Expression Using(Expression disposable, Expression body)
        {
            Expression disposeCall;
            if (typeof(IDisposable).IsAssignableFrom(disposable.Type))
            {
                disposeCall = Call(disposable, DisposeMethod);
            }
            else
            {
                if (disposable.Type.IsValueType)
                {
                    return body;
                }
                var disposableVariable = Variable(typeof(IDisposable), "disposableVariable");
                var assignDisposable = Assign(disposableVariable, TypeAs(disposable, typeof(IDisposable)));
                disposeCall = Block(new[] { disposableVariable }, assignDisposable, IfNullElse(disposableVariable, Empty, Call(disposableVariable, DisposeMethod)));
            }
            return TryFinally(body, disposeCall);
        }
        public static Expression IfNullElse(this Expression expression, Expression then, Expression @else = null)
        {
            var nonNullElse = ToType(@else ?? Default(then.Type), then.Type);
            if(expression.Type.IsValueType && !expression.Type.IsNullableType())
            {
                return nonNullElse;
            }
            return Condition(Equal(expression, Null), then, nonNullElse);
        }
        internal class ReplaceVisitor : ExpressionVisitor
        {
            public Expression NewNode { get; set; }
            public Expression OldNode { get; set; }
        }
        internal class ConvertingVisitor : ReplaceVisitor
        {
            protected override Expression VisitMember(MemberExpression node)
            {
                if (node.Expression == OldNode)
                {
                    node = MakeMemberAccess(ToType(NewNode, OldNode.Type), node.Member);
                }
                return base.VisitMember(node);
            }
            protected override Expression VisitParameter(ParameterExpression node) => node == OldNode ? ToType(NewNode, OldNode.Type) : base.VisitParameter(node);
            protected override Expression VisitMethodCall(MethodCallExpression node)
            {
                if (node.Object == OldNode)
                {
                    node = Call(ToType(NewNode, OldNode.Type), node.Method, node.Arguments);
                }
                return base.VisitMethodCall(node);
            }
        }
        internal class ReplaceExpressionVisitor : ReplaceVisitor
        {
            public override Expression Visit(Expression node)
            {
                if (OldNode == node)
                {
                    node = NewNode;
                }
                return base.Visit(node);
            }
        }
        internal class ExpressionConcatVisitor : ExpressionVisitor
        {
            private readonly LambdaExpression _overrideExpression;
            public ExpressionConcatVisitor(LambdaExpression overrideExpression) => _overrideExpression = overrideExpression;
            public override Expression Visit(Expression node)
            {
                if (_overrideExpression == null)
                    return node;
                if (node.NodeType != ExpressionType.Lambda && node.NodeType != ExpressionType.Parameter)
                {
                    var expression = node;
                    if (node.Type == typeof(object))
                        expression = Convert(node, _overrideExpression.Parameters[0].Type);

                    return ReplaceParameters(_overrideExpression, expression);
                }
                return base.Visit(node);
            }
            protected override Expression VisitLambda<T>(Expression<T> node) => Expression.Lambda(Visit(node.Body), node.Parameters);
        }
    }
}