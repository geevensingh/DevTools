namespace JsonViewer.Model
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Script.Serialization;
    using Utilities;

    internal class JsonObjectFactory : IDisposable
    {
        private CancellationTokenSource _refreshCancellationTokenSource = new CancellationTokenSource();

        public static Task<DeserializeResult> TryDeserialize(string jsonString)
        {
            return Task.Run<DeserializeResult>(async () =>
            {
                jsonString = jsonString.Trim();

                DeserializeResult result = await TrySimpleDeserialize(jsonString);
                if (result.IsSuccessful())
                {
                    return result;
                }

                if (jsonString.StartsWith("\"") && jsonString.EndsWith("\""))
                {
                    string quoteEscaped = jsonString.Substring(1, jsonString.Length - 2).Replace("\"\"", "\"");
                    result = await TrySimpleDeserialize(quoteEscaped);
                    if (result.IsSuccessful())
                    {
                        return result;
                    }
                }

                int firstBrace = jsonString.IndexOfAny(new char[] { '{' });

                if (firstBrace >= 0)
                {
                    foreach (string str in new string[] { jsonString, CSEscape.Unescape(jsonString) })
                    {
                        Dictionary<string, object> dictionary = TryStrictDeserialize(str);
                        if (dictionary != null)
                        {
                            return new DeserializeResult(dictionary);
                        }

                        result = TryTrimmedDeserialize(str, "{", "}", "{0}");
                        if (result.IsSuccessful())
                        {
                            return result;
                        }
                    }
                }

                if (firstBrace != 0)
                {
                    DeserializeResult wrappedResult = await TryDeserialize("{" + jsonString + "}");
                    if (wrappedResult.IsSuccessful())
                    {
                        return wrappedResult;
                    }
                }

                return null;
            });
        }

        public static Task<DeserializeResult> TryAgressiveDeserialize(string jsonString, bool tryDecompress = true)
        {
            return Task.Run<DeserializeResult>(async () =>
            {
                jsonString = jsonString.Trim();

                DeserializeResult result = await TrySimpleDeserialize(jsonString);
                if (result.IsSuccessful())
                {
                    return result;
                }

                // Xpert has a habit of injecting weird things like "1 in the middle of a string.
                // For example: me.CompilerServices.TaskAwaiter"1.GetResult()\\r\\n   at
                string adjustedJsonString = jsonString;
                for (int ii = 0; ii < adjustedJsonString.Length - 3; ii++)
                {
                    if ((adjustedJsonString[ii] != ' ') &&
                        (adjustedJsonString[ii + 1] == '\"') &&
                        (adjustedJsonString[ii + 2] >= '0' && adjustedJsonString[ii + 2] <= '9'))
                    {
                        adjustedJsonString = adjustedJsonString.Remove(ii + 1, 2);
                    }
                }

                result = await TrySimpleDeserialize(adjustedJsonString);
                if (result.IsSuccessful())
                {
                    return result;
                }

                result = await TryDeserialize(jsonString);

                if (result.IsSuccessful())
                {
                    return result;
                }

                if (tryDecompress)
                {
                    try
                    {
                        bool decodableJsonEntity =
                            jsonString.StartsWith("0x4465666C6174654A736F6E456E74697479", StringComparison.InvariantCultureIgnoreCase) || // StringToHex("DeflateJsonEntity")
                            jsonString.StartsWith("RGVmbGF0ZUpzb25FbnRpdHk", StringComparison.InvariantCultureIgnoreCase) || // Base64("DeflateJsonEntity")
                            jsonString.StartsWith("0x4A736F6E456E74697479", StringComparison.InvariantCultureIgnoreCase) || // StringToHex("JsonEntity")
                            jsonString.StartsWith("SnNvbkVudGl0eQ", StringComparison.InvariantCultureIgnoreCase); // Base64("JsonEntity")

                        if (decodableJsonEntity)
                        {
                            byte[] data;
                            if (jsonString.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                            {
                                data = StringHelper.HexStringToByteArray(jsonString);
                            }
                            else
                            {
                                data = Convert.FromBase64String(jsonString);
                            }

                            using (MemoryStream memoryStream = new MemoryStream(data))
                            {
                                string serializationProtocol = StreamHelper.ReadUTF8Line(memoryStream);

                                if (serializationProtocol == "DeflateJsonEntity")
                                {
                                    using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionMode.Decompress))
                                    {
                                        using (StreamReader streamReader = new StreamReader(deflateStream))
                                        {
                                            return await TryAgressiveDeserialize(await streamReader.ReadToEndAsync(), tryDecompress: false);
                                        }
                                    }
                                }
                                else if (serializationProtocol == "JsonEntity")
                                {
                                    using (StreamReader streamReader = new StreamReader(memoryStream))
                                    {
                                        return await TryAgressiveDeserialize(await streamReader.ReadToEndAsync(), tryDecompress: false);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception)
                    { // oops, must not have been decompressable, ignore
                    }
                }

                return result;
            });
        }

        public static Task<DeserializeResult> TrySimpleDeserialize(string jsonString)
        {
            return Task.Run<DeserializeResult>(async () =>
            {
                jsonString = jsonString.Trim();

                int firstBrace = jsonString.IndexOfAny(new char[] { '{', '[' });

                if (firstBrace >= 0)
                {
                    foreach (string str in new string[] { jsonString, CSEscape.Unescape(jsonString) })
                    {
                        Dictionary<string, object> dictionary = TryStrictDeserialize(str);
                        if (dictionary != null)
                        {
                            return new DeserializeResult(dictionary);
                        }
                    }
                }

                if (firstBrace != 0)
                {
                    DeserializeResult wrappedResult = await TrySimpleDeserialize("{" + jsonString + "}");
                    if (wrappedResult != null)
                    {
                        return wrappedResult;
                    }
                }

                return null;
            });
        }

        public static void Flatten(ref List<JsonObject> items, Dictionary<string, object> dictionary, JsonObject parent)
        {
            List<JsonObject> children = new List<JsonObject>();
            foreach (string key in dictionary.Keys)
            {
                object rawObject = dictionary[key];

                JsonObject data = new JsonObject(key, rawObject, parent);
                children.Add(data);

                if (parent == null)
                {
                    items.Add(data);
                }

                if (rawObject != null)
                {
                    if (rawObject is Dictionary<string, object>)
                    {
                        Flatten(ref items, rawObject as Dictionary<string, object>, data);
                    }
                    else if (rawObject is System.Collections.ArrayList)
                    {
                        Flatten(ref items, rawObject as System.Collections.ArrayList, data);
                    }
                }
            }

            parent.SetChildren(children);
        }

        public static void Flatten(ref List<JsonObject> items, System.Collections.ArrayList arrayList, JsonObject parent)
        {
            List<JsonObject> children = new List<JsonObject>();
            for (int ii = 0; ii < arrayList.Count; ii++)
            {
                object rawObject = arrayList[ii];

                JsonObject data = new JsonObject("[" + ii + "]", rawObject, parent);
                children.Add(data);

                if (parent == null)
                {
                    items.Add(data);
                }

                if (rawObject is Dictionary<string, object>)
                {
                    Flatten(ref items, rawObject as Dictionary<string, object>, data);
                }
                else if (rawObject is System.Collections.ArrayList)
                {
                    Flatten(ref items, rawObject as System.Collections.ArrayList, data);
                }
            }

            parent.SetChildren(children);
        }

        public void Dispose()
        {
            _refreshCancellationTokenSource.Cancel();
            _refreshCancellationTokenSource.Dispose();
        }

        private static DeserializeResult TryTrimmedDeserialize(string jsonString, string start, string end, string format)
        {
            IList<string> parts = StringHelper.SplitString(jsonString, start, end);
            if (parts == null)
            {
                return null;
            }

            FileLogger.Assert(parts.Count == 3);
            FileLogger.Assert(!string.IsNullOrEmpty(parts[1]));
            string trimmedString = string.Format(format, parts[1]);
            Dictionary<string, object> result = TryStrictDeserialize(trimmedString);
            if (result == null)
            {
                return null;
            }

            return new DeserializeResult(result, parts[0].Trim(), parts[2].Trim());
        }

        private static Dictionary<string, object> TryStrictDeserialize(string jsonString, bool retryArgumentException = true)
        {
            try
            {
                return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(jsonString);
            }
            catch (ArgumentException e)
            {
                if (retryArgumentException && (e.Message == "Invalid JSON primitive: False." || e.Message == "Invalid JSON primitive: True."))
                {
                    return TryStrictDeserialize(
                        jsonString.Replace("False", "false").Replace("True", "true"),
                        retryArgumentException: false);
                }
            }
            catch (SystemException)
            {
            }

            return null;
        }
    }
}
