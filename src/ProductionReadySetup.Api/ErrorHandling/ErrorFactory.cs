namespace ProductionReadySetup.Api.ErrorHandling
{
    public static  class ErrorFactory
    {
        public static ErrorDescriptor Create(string code, string title, int status)
        {
            // ProblemDetails "type" should be a stable identifier.
            // URN avoids fake URLs and can later be mapped to documentation.
            var type = $"urn:problem:{code.Replace('.', ':')}";

            return new ErrorDescriptor(code, title, status, type);
        }
    }
}
