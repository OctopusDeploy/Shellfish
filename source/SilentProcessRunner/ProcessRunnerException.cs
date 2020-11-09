using System;
using System.Collections.Generic;
using System.Text;

namespace Octopus.SilentProcessRunner
{
    public class ProcessRunnerException : Exception
    {
        readonly int exitCode;

        internal ProcessRunnerException(int exitCode, List<string> errors)
        {
            this.exitCode = exitCode;
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }

        public override string Message
        {
            get
            {
                var sb = new StringBuilder(base.Message);

                sb.AppendFormat(" Exit code: {0}", exitCode);
                if (Errors.Count > 0)
                    sb.AppendLine(string.Join(Environment.NewLine, Errors));
                return sb.ToString();
            }
        }
    }
}