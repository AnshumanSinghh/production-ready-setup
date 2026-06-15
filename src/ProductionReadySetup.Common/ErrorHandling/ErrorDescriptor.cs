namespace ProductionReadySetup.Common.ErrorHandling
{
    public sealed record ErrorDescriptor(
        string Code,
        string Title,
        int Status,
        string Type
        );
}
