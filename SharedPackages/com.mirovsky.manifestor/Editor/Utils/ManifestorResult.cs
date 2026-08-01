public readonly struct ManifestorResult
{
    public readonly bool success;
    public readonly string message;

    private ManifestorResult(bool success, string message)
    {
        this.success = success;
        this.message = message;
    }

    public static ManifestorResult Ok()
    {
        return new ManifestorResult(true, string.Empty);
    }

    public static ManifestorResult Error(string message)
    {
        return new ManifestorResult(false, message);
    }
}
