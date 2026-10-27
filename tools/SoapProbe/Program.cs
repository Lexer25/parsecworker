using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;

namespace SoapProbe
{
    internal static class Program
    {
        private const string Url = "http://127.0.0.1:10101/IntegrationService/IntegrationService.asmx";
        private const string Ns = "http://parsec.ru/Parsec3IntergationService";
        private const string Xsi = "http://www.w3.org/2001/XMLSchema-instance";
        private const string Domain = "SYSTEM";
        private const string User = "parsec";
        private const string Password = "parsec";
        private const string Card = "12121212";

        private static string CustomFrom;
        private static string CustomTo;

        private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "probe.log");
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        private static void Log(string text)
        {
            File.AppendAllText(LogPath, text + Environment.NewLine);
            Console.WriteLine(text);
        }

        private static XDocument Call(string action, string body)
        {
            string envelope =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<soap:Envelope xmlns:xsi=\"" + Xsi + "\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<soap:Body>" + body + "</soap:Body></soap:Envelope>";

            var request = new HttpRequestMessage(HttpMethod.Post, Url);
            request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
            request.Headers.TryAddWithoutValidation("SOAPAction", "\"" + action + "\"");

            Log("REQUEST " + action + ":" + Environment.NewLine + body);

            HttpResponseMessage response;
            try
            {
                response = Http.SendAsync(request).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log("HTTP EXCEPTION: " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }

            string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Log("RESPONSE " + (int)response.StatusCode + ":" + Environment.NewLine + text);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + text);
            }

            return XDocument.Parse(text);
        }

        private static XElement El(XContainer node, string name)
        {
            return node == null ? null : node.Elements(XName.Get(name, Ns)).FirstOrDefault();
        }

        private static int Int(XElement parent, string name)
        {
            XElement el = El(parent, name);
            return el == null ? 0 : int.Parse(el.Value, CultureInfo.InvariantCulture);
        }

        private static string Str(XElement parent, string name)
        {
            XElement el = El(parent, name);
            return el == null ? null : el.Value;
        }

        private static string ResultOf(XDocument doc, string name)
        {
            XElement result = doc.Descendants(XName.Get(name, Ns)).FirstOrDefault();
            if (result == null)
            {
                return "NO RESULT ELEMENT";
            }

            return "Result=" + Int(result, "Result") + " Error=" + Str(result, "ErrorMessage");
        }

        private static Guid GuidOf(XDocument doc, string name)
        {
            XElement result = doc.Descendants(XName.Get(name, Ns)).FirstOrDefault();
            XElement value = El(result, "Value");
            return value == null || string.IsNullOrWhiteSpace(value.Value)
                ? Guid.Empty
                : Guid.Parse(value.Value);
        }

        private sealed class IdentifierState
        {
            public string Code;
            public string PersonId;
            public bool IsPrimary;
            public string AccGroupId;
            public long PrivilegeMask;
            public int IdentType;
            public string Name;
            public string ValidFrom;
            public string ValidTo;

            public string ToXmlRequest(string fromXml, string toXml)
            {
                return
                    "<identifier xsi:type=\"IdentifierTemp\">" +
                    "<CODE>" + Code + "</CODE>" +
                    "<PERSON_ID>" + PersonId + "</PERSON_ID>" +
                    "<IS_PRIMARY>" + (IsPrimary ? "true" : "false") + "</IS_PRIMARY>" +
                    "<ACCGROUP_ID>" + AccGroupId + "</ACCGROUP_ID>" +
                    "<PRIVILEGE_MASK>" + PrivilegeMask + "</PRIVILEGE_MASK>" +
                    "<IDENTIFTYPE>" + IdentType + "</IDENTIFTYPE>" +
                    "<NAME>" + (Name ?? string.Empty) + "</NAME>" +
                    fromXml + toXml +
                    "</identifier>";
            }
        }

        private static string DateXml(string element, string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return "<" + element + ">" + value + "</" + element + ">";
        }

        private static string NilXml(string element)
        {
            return "<" + element + " xsi:nil=\"true\" />";
        }

        private static IdentifierState ParseIdentifier(XElement id)
        {
            var state = new IdentifierState
            {
                Code = Str(id, "CODE"),
                PersonId = Str(id, "PERSON_ID"),
                IsPrimary = string.Equals(Str(id, "IS_PRIMARY"), "true", StringComparison.OrdinalIgnoreCase),
                AccGroupId = Str(id, "ACCGROUP_ID"),
                PrivilegeMask = long.Parse(Str(id, "PRIVILEGE_MASK") ?? "0", CultureInfo.InvariantCulture),
                IdentType = int.Parse(Str(id, "IDENTIFTYPE") ?? "0", CultureInfo.InvariantCulture),
                Name = Str(id, "NAME")
            };

            XElement from = El(id, "VALID_FROM");
            XElement to = El(id, "VALID_TO");
            state.ValidFrom = DateValue(from);
            state.ValidTo = DateValue(to);
            return state;
        }

        private static string DateValue(XElement element)
        {
            if (element == null)
            {
                return null;
            }

            XAttribute nil = element.Attribute(XName.Get("nil", Xsi));
            if (nil != null && string.Equals(nil.Value, "true", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(element.Value) ? null : element.Value;
        }

        private static IdentifierState GetIdentifier(Guid sessionId, Guid personId, string code)
        {
            XDocument ids = Call("http://parsec.ru/Parsec3IntergationService/GetPersonIdentifiers",
                "<GetPersonIdentifiers xmlns=\"" + Ns + "\">" +
                "<sessionID>" + sessionId + "</sessionID>" +
                "<personID>" + personId + "</personID>" +
                "</GetPersonIdentifiers>");

            XElement array = El(ids.Descendants(XName.Get("GetPersonIdentifiersResponse", Ns)).First(), "GetPersonIdentifiersResult");
            if (array == null)
            {
                return null;
            }

            foreach (XElement id in array.Elements(XName.Get("Identifier", Ns)))
            {
                string value = Str(id, "CODE");
                if (string.Equals(value, code, StringComparison.OrdinalIgnoreCase))
                {
                    Log("IDENTIFIER XML: " + id.ToString(SaveOptions.DisableFormatting));
                    return ParseIdentifier(id);
                }
            }

            return null;
        }

        private static int Main(string[] args)
        {
            if (File.Exists(LogPath))
            {
                File.Delete(LogPath);
            }

            string mode = args.Length == 0 ? "read" : args[0];
            CustomFrom = args.Length > 2 ? args[2] : null;
            CustomTo = args.Length > 3 ? args[3] : null;
            string card = args.Length > 1 ? args[1] : Card;
            string code = card.Length == 8 && card.All(Uri.IsHexDigit)
                ? card.ToUpperInvariant()
                : Convert.ToInt64(card).ToString("X8");
            Log("MODE=" + mode + " card " + card + " -> code " + code);

            XDocument open = Call("http://parsec.ru/Parsec3IntergationService/OpenSession",
                "<OpenSession xmlns=\"" + Ns + "\">" +
                "<domain>" + Domain + "</domain>" +
                "<userName>" + User + "</userName>" +
                "<password>" + Password + "</password>" +
                "</OpenSession>");

            XElement sessionResult = open.Descendants(XName.Get("OpenSessionResult", Ns)).First();
            int result = Int(sessionResult, "Result");
            XElement sessionValue = El(sessionResult, "Value");
            Guid sessionId = sessionValue == null ? Guid.Empty : Guid.Parse(Str(sessionValue, "SessionID"));

            Log("OpenSession: " + ResultOf(open, "OpenSessionResult") + " Session=" + sessionId);
            if (result != 0 || sessionId == Guid.Empty)
            {
                return 2;
            }

            try
            {
                XDocument find = Call("http://parsec.ru/Parsec3IntergationService/FindPersonByIdentifier",
                    "<FindPersonByIdentifier xmlns=\"" + Ns + "\">" +
                    "<sessionID>" + sessionId + "</sessionID>" +
                    "<cardCode>" + code + "</cardCode>" +
                    "</FindPersonByIdentifier>");

                XElement person = find.Descendants(XName.Get("FindPersonByIdentifierResult", Ns)).FirstOrDefault();
                if (person == null)
                {
                    Log("Person NOT FOUND");
                    return 3;
                }

                Guid personId = Guid.Parse(Str(person, "ID"));
                Log("Person ID=" + personId + " LAST=" + Str(person, "LAST_NAME"));

                IdentifierState original = GetIdentifier(sessionId, personId, code);
                if (original == null)
                {
                    Log("Identifier NOT FOUND");
                    return 4;
                }

                Log("Original: FROM=" + (original.ValidFrom ?? "<null>") + " TO=" + (original.ValidTo ?? "<null>")
                    + " GROUP=" + original.AccGroupId + " MASK=" + original.PrivilegeMask);

                if (mode == "read")
                {
                    return 0;
                }

                if (mode == "extra")
                {
                    XDocument extra = Call("http://parsec.ru/Parsec3IntergationService/GetIdentifierExtraData",
                        "<GetIdentifierExtraData xmlns=\"" + Ns + "\">" +
                        "<sessionID>" + sessionId + "</sessionID>" +
                        "<cardCode>" + code + "</cardCode>" +
                        "</GetIdentifierExtraData>");
                    Log("EXTRA RESULT: " + (extra.ToString(SaveOptions.DisableFormatting)));
                    return 0;
                }

                XDocument edit = Call("http://parsec.ru/Parsec3IntergationService/OpenPersonEditingSession",
                    "<OpenPersonEditingSession xmlns=\"" + Ns + "\">" +
                    "<sessionID>" + sessionId + "</sessionID>" +
                    "<personID>" + personId + "</personID>" +
                    "</OpenPersonEditingSession>");

                Guid editSession = GuidOf(edit, "OpenPersonEditingSessionResult");
                Log("OpenPersonEditingSession: " + ResultOf(edit, "OpenPersonEditingSessionResult")
                    + " EditSession=" + editSession);
                if (editSession == Guid.Empty)
                {
                    return 5;
                }

                try
                {
                    RunExperiment(sessionId, editSession, personId, code, original, mode);
                }
                finally
                {
                    // Restore the original state if the experiment changed it.
                    IdentifierState current = GetIdentifier(sessionId, personId, code);
                    if (mode != "setminfrom_persist" && mode != "setunlimited_persist"
                        && mode != "setmax_persist" && mode != "setdates_persist"
                        && current != null && !SameState(current, original))
                    {
                        Log("RESTORE original identifier state");
                        ChangeIdentifier(editSession, original,
                            DateXml("VALID_FROM", original.ValidFrom),
                            DateXml("VALID_TO", original.ValidTo));
                        GetIdentifier(sessionId, personId, code);
                    }

                    Call("http://parsec.ru/Parsec3IntergationService/ClosePersonEditingSession",
                        "<ClosePersonEditingSession xmlns=\"" + Ns + "\">" +
                        "<personEditSessionID>" + editSession + "</personEditSessionID>" +
                        "</ClosePersonEditingSession>");
                }
            }
            finally
            {
                Call("http://parsec.ru/Parsec3IntergationService/CloseSession",
                    "<CloseSession xmlns=\"" + Ns + "\"><sessionID>" + sessionId + "</sessionID></CloseSession>");
            }

            return 0;
        }

        private static void RunExperiment(Guid sessionId, Guid editSession, Guid personId,
            string code, IdentifierState original, string mode)
        {
            string from = DateXml("VALID_FROM", original.ValidFrom);
            string to = DateXml("VALID_TO", original.ValidTo);

            switch (mode)
            {
                case "baseline":
                    ChangeIdentifier(editSession, original, from, to);
                    break;

                case "omitfrom":
                    ChangeIdentifier(editSession, original, string.Empty, to);
                    break;

                case "omitto":
                    ChangeIdentifier(editSession, original, from, string.Empty);
                    break;

                case "bothomit":
                    ChangeIdentifier(editSession, original, string.Empty, string.Empty);
                    break;

                case "nilfrom":
                    ChangeIdentifier(editSession, original, NilXml("VALID_FROM"), to);
                    break;

                case "nilto":
                    ChangeIdentifier(editSession, original, from, NilXml("VALID_TO"));
                    break;

                case "bothnil":
                    ChangeIdentifier(editSession, original, NilXml("VALID_FROM"), NilXml("VALID_TO"));
                    break;

                case "minfrom":
                    ChangeIdentifier(editSession, original,
                        DateXml("VALID_FROM", "0001-01-01T00:00:00"), to);
                    break;

                case "minto":
                    ChangeIdentifier(editSession, original,
                        from, DateXml("VALID_TO", "0001-01-01T00:00:00"));
                    break;

                case "setminfrom_persist":
                    ChangeIdentifier(editSession, original,
                        DateXml("VALID_FROM", "0001-01-01T00:00:00"), to);
                    break;

                case "setdates_persist":
                    ChangeIdentifier(editSession, original,
                        DateXml("VALID_FROM", CustomFrom),
                        DateXml("VALID_TO", CustomTo));
                    break;

                case "setmax_persist":
                    ChangeIdentifier(editSession, original,
                        DateXml("VALID_FROM", "9999-12-31T00:00:00"),
                        DateXml("VALID_TO", "9999-12-31T00:00:00"));
                    break;

                case "setunlimited_persist":
                    ChangeRaw(editSession,
                        "<identifier xsi:type=\"Identifier\">" +
                        "<CODE>" + original.Code + "</CODE>" +
                        "<PERSON_ID>" + original.PersonId + "</PERSON_ID>" +
                        "<IS_PRIMARY>" + (original.IsPrimary ? "true" : "false") + "</IS_PRIMARY>" +
                        "<ACCGROUP_ID>" + original.AccGroupId + "</ACCGROUP_ID>" +
                        "<PRIVILEGE_MASK>" + original.PrivilegeMask + "</PRIVILEGE_MASK>" +
                        "<IDENTIFTYPE>" + original.IdentType + "</IDENTIFTYPE>" +
                        "<NAME>" + (original.Name ?? string.Empty) + "</NAME>" +
                        "</identifier>");
                    break;

                case "changebase":
                    ChangeRaw(editSession,
                        "<identifier>" +
                        "<CODE>" + original.Code + "</CODE>" +
                        "<PERSON_ID>" + original.PersonId + "</PERSON_ID>" +
                        "<IS_PRIMARY>" + (original.IsPrimary ? "true" : "false") + "</IS_PRIMARY>" +
                        "</identifier>");
                    break;

                case "changeident":
                    ChangeRaw(editSession,
                        "<identifier xsi:type=\"Identifier\">" +
                        "<CODE>" + original.Code + "</CODE>" +
                        "<PERSON_ID>" + original.PersonId + "</PERSON_ID>" +
                        "<IS_PRIMARY>" + (original.IsPrimary ? "true" : "false") + "</IS_PRIMARY>" +
                        "<ACCGROUP_ID>" + original.AccGroupId + "</ACCGROUP_ID>" +
                        "<PRIVILEGE_MASK>" + original.PrivilegeMask + "</PRIVILEGE_MASK>" +
                        "<IDENTIFTYPE>" + original.IdentType + "</IDENTIFTYPE>" +
                        "<NAME>" + (original.Name ?? string.Empty) + "</NAME>" +
                        "</identifier>");
                    break;

                case "addtemp_from9999":
                    AddTempIdentifier(sessionId, editSession, personId, original,
                        "0F0F0F0C", DateXml("VALID_FROM", "9999-12-31T00:00:00"), string.Empty);
                    break;

                case "addtemp_from":
                    AddTempIdentifier(sessionId, editSession, personId, original,
                        "0F0F0F0E", DateXml("VALID_FROM", original.ValidFrom), string.Empty);
                    break;

                case "addtemp_to":
                    AddTempIdentifier(sessionId, editSession, personId, original,
                        "0F0F0F0E", string.Empty, DateXml("VALID_TO", original.ValidTo));
                    break;

                case "addtemp_none":
                    AddTempIdentifier(sessionId, editSession, personId, original,
                        "0F0F0F0E", string.Empty, string.Empty);
                    break;

                case "addident":
                    AddIdentifierWithGroup(sessionId, editSession, personId, original, "0F0F0F0D");
                    break;

                case "addbase":
                    AddBaseIdentifier(sessionId, editSession, personId, "0F0F0F0F");
                    break;

                case "from1700":
                    ChangeIdentifier(editSession, original,
                        DateXml("VALID_FROM", "1700-01-01T00:00:00"), to);
                    break;

                case "maxto":
                    ChangeIdentifier(editSession, original,
                        from, DateXml("VALID_TO", "9999-12-31T23:59:59"));
                    break;

                case "minfrom_maxto":
                    ChangeIdentifier(editSession, original,
                        DateXml("VALID_FROM", "0001-01-01T00:00:00"),
                        DateXml("VALID_TO", "9999-12-31T23:59:59"));
                    break;

                case "omitfrom_maxto":
                    ChangeIdentifier(editSession, original,
                        string.Empty, DateXml("VALID_TO", "9999-12-31T23:59:59"));
                    break;

                case "all":
                    RunExperiment(sessionId, editSession, personId, code, original, "baseline");
                    RunExperiment(sessionId, editSession, personId, code, original, "omitfrom");
                    RunExperiment(sessionId, editSession, personId, code, original, "omitto");
                    RunExperiment(sessionId, editSession, personId, code, original, "nilfrom");
                    RunExperiment(sessionId, editSession, personId, code, original, "nilto");
                    RunExperiment(sessionId, editSession, personId, code, original, "bothomit");
                    RunExperiment(sessionId, editSession, personId, code, original, "bothnil");
                    RunExperiment(sessionId, editSession, personId, code, original, "minfrom");
                    RunExperiment(sessionId, editSession, personId, code, original, "minto");
                    break;

                default:
                    Log("Unknown mode: " + mode);
                    break;
            }

            GetIdentifier(sessionId, personId, code);
        }

        private static void AddIdentifierWithGroup(Guid sessionId, Guid editSession, Guid personId,
            IdentifierState original, string code)
        {
            try
            {
                XDocument add = Call("http://parsec.ru/Parsec3IntergationService/AddPersonIdentifier",
                    "<AddPersonIdentifier xmlns=\"" + Ns + "\">" +
                    "<personEditSessionID>" + editSession + "</personEditSessionID>" +
                    "<identifier xsi:type=\"Identifier\">" +
                    "<CODE>" + code + "</CODE>" +
                    "<PERSON_ID>" + personId + "</PERSON_ID>" +
                    "<IS_PRIMARY>false</IS_PRIMARY>" +
                    "<ACCGROUP_ID>" + original.AccGroupId + "</ACCGROUP_ID>" +
                    "<PRIVILEGE_MASK>" + original.PrivilegeMask + "</PRIVILEGE_MASK>" +
                    "<IDENTIFTYPE>" + original.IdentType + "</IDENTIFTYPE>" +
                    "<NAME>" + (original.Name ?? string.Empty) + "</NAME>" +
                    "</identifier>" +
                    "</AddPersonIdentifier>");

                Log("AddPersonIdentifier Identifier (no dates): " + ResultOf(add, "AddPersonIdentifierResult"));
                IdentifierState added = GetIdentifier(sessionId, personId, code);
                Log("Identifier added: " + (added == null
                    ? "NOT FOUND"
                    : "FROM=" + Nullable(added.ValidFrom) + " TO=" + Nullable(added.ValidTo)
                      + " GROUP=" + added.AccGroupId));
            }
            finally
            {
                Call("http://parsec.ru/Parsec3IntergationService/DeleteIdentifier",
                    "<DeleteIdentifier xmlns=\"" + Ns + "\">" +
                    "<sessionID>" + sessionId + "</sessionID>" +
                    "<Code>" + code + "</Code>" +
                    "</DeleteIdentifier>");
            }
        }

        private static void AddBaseIdentifier(Guid sessionId, Guid editSession, Guid personId, string code)
        {
            XDocument add = Call("http://parsec.ru/Parsec3IntergationService/AddPersonIdentifier",
                "<AddPersonIdentifier xmlns=\"" + Ns + "\">" +
                "<personEditSessionID>" + editSession + "</personEditSessionID>" +
                "<identifier>" +
                "<CODE>" + code + "</CODE>" +
                "<PERSON_ID>" + personId + "</PERSON_ID>" +
                "<IS_PRIMARY>false</IS_PRIMARY>" +
                "</identifier>" +
                "</AddPersonIdentifier>");

            Log("AddPersonIdentifier base (no dates): " + ResultOf(add, "AddPersonIdentifierResult"));
            IdentifierState added = GetIdentifier(sessionId, personId, code);
            Log("Added identifier: " + (added == null
                ? "NOT FOUND"
                : "FROM=" + Nullable(added.ValidFrom) + " TO=" + Nullable(added.ValidTo)
                  + " GROUP=" + added.AccGroupId));

            XDocument delete = Call("http://parsec.ru/Parsec3IntergationService/DeleteIdentifier",
                "<DeleteIdentifier xmlns=\"" + Ns + "\">" +
                "<sessionID>" + sessionId + "</sessionID>" +
                "<Code>" + code + "</Code>" +
                "</DeleteIdentifier>");
            Log("DeleteIdentifier: " + ResultOf(delete, "DeleteIdentifierResult"));
            Log("After delete: " + (GetIdentifier(sessionId, personId, code) == null ? "NOT FOUND" : "STILL PRESENT"));
        }

        private static bool SameState(IdentifierState left, IdentifierState right)
        {
            return left.ValidFrom == right.ValidFrom
                   && left.ValidTo == right.ValidTo
                   && left.AccGroupId == right.AccGroupId
                   && left.PrivilegeMask == right.PrivilegeMask
                   && left.IdentType == right.IdentType
                   && left.Name == right.Name
                   && left.IsPrimary == right.IsPrimary;
        }

        private static void ChangeRaw(Guid editSession, string identifierXml)
        {
            XDocument change = Call("http://parsec.ru/Parsec3IntergationService/ChangePersonIdentifier",
                "<ChangePersonIdentifier xmlns=\"" + Ns + "\">" +
                "<personEditSessionID>" + editSession + "</personEditSessionID>" +
                identifierXml +
                "</ChangePersonIdentifier>");

            Log("ChangePersonIdentifier: " + ResultOf(change, "ChangePersonIdentifierResult"));
        }

        private static void AddTempIdentifier(Guid sessionId, Guid editSession, Guid personId,
            IdentifierState original, string code, string fromXml, string toXml)
        {
            try
            {
                XDocument add = Call("http://parsec.ru/Parsec3IntergationService/AddPersonIdentifier",
                    "<AddPersonIdentifier xmlns=\"" + Ns + "\">" +
                    "<personEditSessionID>" + editSession + "</personEditSessionID>" +
                    "<identifier xsi:type=\"IdentifierTemp\">" +
                    "<CODE>" + code + "</CODE>" +
                    "<PERSON_ID>" + personId + "</PERSON_ID>" +
                    "<IS_PRIMARY>false</IS_PRIMARY>" +
                    "<ACCGROUP_ID>" + original.AccGroupId + "</ACCGROUP_ID>" +
                    "<PRIVILEGE_MASK>" + original.PrivilegeMask + "</PRIVILEGE_MASK>" +
                    "<IDENTIFTYPE>" + original.IdentType + "</IDENTIFTYPE>" +
                    "<NAME>" + (original.Name ?? string.Empty) + "</NAME>" +
                    fromXml + toXml +
                    "</identifier>" +
                    "</AddPersonIdentifier>");

                Log("AddPersonIdentifier temp: " + ResultOf(add, "AddPersonIdentifierResult"));
                IdentifierState added = GetIdentifier(sessionId, personId, code);
                Log("Temp identifier: " + (added == null
                    ? "NOT FOUND"
                    : "FROM=" + Nullable(added.ValidFrom) + " TO=" + Nullable(added.ValidTo)
                      + " GROUP=" + added.AccGroupId));
            }
            finally
            {
                XDocument delete = Call("http://parsec.ru/Parsec3IntergationService/DeleteIdentifier",
                    "<DeleteIdentifier xmlns=\"" + Ns + "\">" +
                    "<sessionID>" + sessionId + "</sessionID>" +
                    "<Code>" + code + "</Code>" +
                    "</DeleteIdentifier>");
                Log("DeleteIdentifier temp: " + ResultOf(delete, "DeleteIdentifierResult"));
                Log("After delete temp: " + (GetIdentifier(sessionId, personId, code) == null
                    ? "NOT FOUND" : "STILL PRESENT"));
            }
        }

        private static void ChangeIdentifier(Guid editSession, IdentifierState state,
            string fromXml, string toXml)
        {
            XDocument change = Call("http://parsec.ru/Parsec3IntergationService/ChangePersonIdentifier",
                "<ChangePersonIdentifier xmlns=\"" + Ns + "\">" +
                "<personEditSessionID>" + editSession + "</personEditSessionID>" +
                state.ToXmlRequest(fromXml, toXml) +
                "</ChangePersonIdentifier>");

            Log("ChangePersonIdentifier: " + ResultOf(change, "ChangePersonIdentifierResult"));
        }

        private static string Nullable(string value)
        {
            return value ?? "<null>";
        }
    }
}
