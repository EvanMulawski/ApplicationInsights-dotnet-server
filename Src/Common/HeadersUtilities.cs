﻿namespace Microsoft.ApplicationInsights.Common
{
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;

    /// <summary>
    /// Generic functions that can be used to get and set Http headers.
    /// </summary>
    public static class HeadersUtilities
    {
        /// <summary>
        /// Get the key value from the provided HttpHeader value that is set up as a comma-separated list of key value pairs. Each key value pair is formatted like (key)=(value).
        /// </summary>
        /// <param name="headerValue">The header values that may contain key name/value pairs.</param>
        /// <param name="keyName">The name of the key value to find in the provided header values.</param>
        /// <returns>The first key value, if it is found. If it is not found, then null.</returns>
        public static string GetHeaderKeyValue(IEnumerable<string> headerValue, string keyName)
        {
            if (headerValue != null)
            {
                foreach (string keyNameValue in headerValue)
                {
                    string[] keyNameValueParts = keyNameValue.Trim().Split('=');
                    if (keyNameValueParts.Length == 2 && keyNameValueParts[0].Trim() == keyName)
                    {
                        return keyNameValueParts[1].Trim();
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Given the provided list of header value strings, return a comma-separated list of key
        /// name/value pairs with the provided keyName and keyValue. If the initial header value
        /// strings contains the key name, then the original key value should be replaced with the
        /// provided key value. If the initial header value strings don't contain the key name,
        /// then the key name/value pair should be added to the comma-separated list and returned.
        /// </summary>
        /// <param name="headerValues">The existing header values that the key/value pair should be added to.</param>
        /// <param name="keyName">The name of the key to add.</param>
        /// <param name="keyValue">The value of the key to add.</param>
        /// <returns>The result of setting the provided key name/value pair into the provided headerValues.</returns>
        public static string SetHeaderKeyValue(IEnumerable<string> headerValues, string keyName, string keyValue)
        {
            StringBuilder result = new StringBuilder();

            bool found = false;
            if (headerValues != null)
            {
                foreach (string keyValuePair in headerValues)
                {
                    int equalsSignIndex = keyValuePair.IndexOf('=');
                    if (equalsSignIndex == -1)
                    {
                        Append(result, keyValuePair.Trim());
                    }
                    else
                    {
                        string name = TrimSubstring(keyValuePair, 0, equalsSignIndex);
                        if (name == null)
                        {
                            Append(result, keyValuePair.Trim());
                        }
                        else if (name != keyName)
                        {
                            string value = TrimSubstring(keyValuePair, equalsSignIndex + 1, keyValuePair.Length);
                            if (value == null)
                            {
                                value = string.Empty;
                            }
                            Append(result, CreateKeyValuePair(name, value));
                        }
                        else if (!found)
                        {
                            found = true;
                            Append(result, CreateKeyValuePair(keyName.Trim(), keyValue.Trim()));
                        }
                    }
                }
            }

            if (!found)
            {
                Append(result, CreateKeyValuePair(keyName.Trim(), keyValue.Trim()));
            }

            return result.ToString();
        }

        /// <summary>
        /// Append the provided toAppend string after the provided value string.
        /// </summary>
        /// <param name="value">The string to append toAppend to.</param>
        /// <param name="toAppend">The string to append after value.</param>
        private static void Append(StringBuilder value, string toAppend)
        {
            if (value.Length > 0)
            {
                value.Append(", ");
            }
            value.Append(toAppend);
        }

        private static string TrimSubstring(string value, int startIndex, int endIndex)
        {
            int firstNonWhitespaceIndex = -1;
            int last = -1;
            for (int firstSearchIndex = startIndex; firstSearchIndex < endIndex; ++firstSearchIndex)
            {
                if (!char.IsWhiteSpace(value[firstSearchIndex]))
                {
                    firstNonWhitespaceIndex = firstSearchIndex;
                    
                    // Found the first non-whitespace character index, now look for the last.
                    for (int lastSearchIndex = endIndex - 1; lastSearchIndex >= startIndex; --lastSearchIndex)
                    {
                        if (!char.IsWhiteSpace(value[lastSearchIndex]))
                        {
                            last = lastSearchIndex;
                            break;
                        }
                    }
                    break;
                }
            }

            return firstNonWhitespaceIndex == -1 ? null : value.Substring(firstNonWhitespaceIndex, last - firstNonWhitespaceIndex + 1);
        }

        private static string CreateKeyValuePair(string key, string value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}={1}", key, value);
        }
    }
}