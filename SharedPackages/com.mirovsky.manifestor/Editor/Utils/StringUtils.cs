namespace Manifestor
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

        public static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
