using System;
using System.Data;
using System.IO;
using System.Text;
using FirebirdSql.Data.FirebirdClient;

namespace DbProbe
{
    internal static class Program
    {
        private const string ConnectionString =
            "User=SYSDBA;Password=temp;Database=localhost:D:\\rrr\\ilove\\ILOVE_2.GDB;" +
            "DataSource=localhost;Port=3050;Dialect=3;Charset=WIN1251;ServerType=0;";

        private static readonly string LogPath =
            Path.Combine(AppContext.BaseDirectory, "dbprobe.log");

        private static void Log(string text)
        {
            File.AppendAllText(LogPath, text + Environment.NewLine);
            Console.WriteLine(text);
        }

        private static int Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (File.Exists(LogPath))
            {
                File.Delete(LogPath);
            }

            try
            {
                using (var connection = new FbConnection(ConnectionString))
                {
                    connection.Open();
                    Log("Connected: " + connection.ServerVersion);

                    ReadCard(connection);

                    if (args.Length > 0 && args[0] == "set")
                    {
                        using (var tx = connection.BeginTransaction())
                        using (var cmd = new FbCommand(
                            "update card set timestart = cast('0001-01-01 00:00:00' as timestamp) " +
                            "where id_card = 12121212", connection, tx))
                        {
                            int affected = cmd.ExecuteNonQuery();
                            Log("UPDATE affected: " + affected);
                            tx.Commit();
                        }

                        ReadCard(connection);
                    }

                    if (args.Length > 0 && args[0] == "set_2030_2026")
                    {
                        using (var tx = connection.BeginTransaction())
                        using (var cmd = new FbCommand(
                            "update card set timestart = cast('2030-10-28 00:00:00' as timestamp), " +
                            "timeend = cast('2026-11-28 00:00:00' as timestamp) where id_card = 12121212",
                            connection, tx))
                        {
                            int affected = cmd.ExecuteNonQuery();
                            Log("UPDATE affected: " + affected);
                            tx.Commit();
                        }

                        ReadCard(connection);
                    }

                    if (args.Length > 0 && args[0] == "set_2000_2026")
                    {
                        using (var tx = connection.BeginTransaction())
                        using (var cmd = new FbCommand(
                            "update card set timestart = cast('2000-10-28 00:00:00' as timestamp), " +
                            "timeend = cast('2026-11-28 00:00:00' as timestamp) where id_card = 12121212",
                            connection, tx))
                        {
                            int affected = cmd.ExecuteNonQuery();
                            Log("UPDATE affected: " + affected);
                            tx.Commit();
                        }

                        ReadCard(connection);
                    }

                    if (args.Length > 0 && args[0] == "set_max")
                    {
                        using (var tx = connection.BeginTransaction())
                        using (var cmd = new FbCommand(
                            "update card set timestart = cast('9999-12-31 00:00:00' as timestamp), " +
                            "timeend = cast('9999-12-31 00:00:00' as timestamp) where id_card = 12121212",
                            connection, tx))
                        {
                            int affected = cmd.ExecuteNonQuery();
                            Log("UPDATE affected: " + affected);
                            tx.Commit();
                        }

                        ReadCard(connection);
                    }

                    if (args.Length > 0 && args[0] == "set_unlimited")
                    {
                        using (var tx = connection.BeginTransaction())
                        using (var cmd = new FbCommand(
                            "update card set timestart = cast('9999-12-31 00:00:00' as timestamp), " +
                            "timeend = null where id_card = 12121212", connection, tx))
                        {
                            int affected = cmd.ExecuteNonQuery();
                            Log("UPDATE affected: " + affected);
                            tx.Commit();
                        }

                        ReadCard(connection);
                    }

                    ReadQueue(connection);
                }
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex.GetType().Name + ": " + ex.Message);
                if (ex.InnerException != null)
                {
                    Log("INNER: " + ex.InnerException.Message);
                }
                return 1;
            }

            return 0;
        }

        private static void ReadCard(FbConnection connection)
        {
            const string sql =
                "select id_card, timestart, timeend, \"ACTIVE\", id_cardtype, id_pep " +
                "from card where id_card = 12121212";

            using (var cmd = new FbCommand(sql, connection))
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read())
                {
                    Log("CARD 12121212 NOT FOUND");
                    return;
                }

                Log("CARD 12121212:");
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    object value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    Log("  " + reader.GetName(i) + " = " +
                        (value == null ? "<NULL>" : value + " (" + value.GetType().Name + ")"));
                }
            }
        }

        private static void ReadQueue(FbConnection connection)
        {
            const string sql =
                "select first 5 id, operation, status, id_pep, id_card, error_text " +
                "from parsec_queue order by id desc";

            try
            {
                using (var cmd = new FbCommand(sql, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    Log("PARSEC_QUEUE last rows:");
                    while (reader.Read())
                    {
                        Log("  id=" + reader.GetValue(0)
                            + " op=" + reader.GetValue(1)
                            + " status=" + reader.GetValue(2)
                            + " id_pep=" + (reader.IsDBNull(3) ? "<NULL>" : reader.GetValue(3))
                            + " id_card=" + (reader.IsDBNull(4) ? "<NULL>" : reader.GetValue(4))
                            + " error=" + (reader.IsDBNull(5) ? "<NULL>" : reader.GetValue(5)));
                    }
                }
            }
            catch (Exception ex)
            {
                Log("QUEUE READ ERROR: " + ex.Message);
            }
        }
    }
}
