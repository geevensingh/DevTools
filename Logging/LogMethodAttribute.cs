using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using PostSharp.Aspects;

namespace Logging
{
    [PostSharp.Serialization.PSerializable]
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class LogMethodAttribute : OnMethodBoundaryAspect
    {
        private LogLevel _logLevel;
        private string _uniqueId = null;

        public LogMethodAttribute()
            : this(LogLevel.Normal)
        {

        }

        public LogMethodAttribute(LogLevel logLevel)
        {
            this._logLevel = logLevel;
        }

        public override void OnEntry(MethodExecutionArgs args)
        {
            Debug.Assert(this._uniqueId == null);
            this._uniqueId = Guid.NewGuid().ToString();

            StringBuilder stringBuilder = new StringBuilder();
            AppendCallInformation(args, stringBuilder);
            Logger.Instance.Start(stringBuilder.ToString(), this._uniqueId, this._logLevel);
        }

        public override void OnException(MethodExecutionArgs args)
        {
            Debug.Fail("got an exception");
        }

        public override void OnSuccess(MethodExecutionArgs args)
        {
            StringBuilder stringBuilder = new StringBuilder();
            AppendCallInformation(args, stringBuilder);
            Logger.Instance.Stop(stringBuilder.ToString(), this._uniqueId, this._logLevel);

            this._uniqueId = null;
        }

        private static void AppendCallInformation(MethodExecutionArgs args, StringBuilder stringBuilder)
        {
            var declaringType = args.Method.DeclaringType;
            AppendTypeName(stringBuilder, declaringType);
            stringBuilder.Append('.');
            stringBuilder.Append(args.Method.Name);

            if (args.Method.IsGenericMethod)
            {
                var genericArguments = args.Method.GetGenericArguments();
                AppendGenericArguments(stringBuilder, genericArguments);
            }

            AppendArguments(stringBuilder, args.Arguments);
        }

        public static void AppendTypeName(StringBuilder stringBuilder, Type declaringType)
        {
            stringBuilder.Append(declaringType.FullName);
            if (declaringType.IsGenericType)
            {
                AppendGenericArguments(stringBuilder, declaringType.GetGenericArguments());
            }
        }

        public static void AppendGenericArguments(StringBuilder stringBuilder, Type[] genericArguments)
        {
            stringBuilder.Append('<');
            stringBuilder.Append(string.Join(", ", genericArguments.Select(a => a.Name)));
            stringBuilder.Append('>');
        }

        public static void AppendArguments(StringBuilder stringBuilder, Arguments arguments)
        {
            stringBuilder.Append('(');
            stringBuilder.Append(string.Join(", ", arguments.Select( (a) =>
            {
                if (a is string s)
                {
                    return '\"' + s + '\"';
                }

                return a.ToString();
            })));
            stringBuilder.Append(')');
        }
    }
}
