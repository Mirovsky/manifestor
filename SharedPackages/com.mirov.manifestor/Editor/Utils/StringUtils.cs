namespace Mirov.Manifestor.Editor
{
    using System.Text.RegularExpressions;

    public static class StringUtils
    {
        public static string ToDisplayName(string typeName)
        {
            var name = typeName[(typeName.LastIndexOf('.') + 1)..];

            return Regex.Replace(
                name,
                "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
                " ");
        }
    }
}
