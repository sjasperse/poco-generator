using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Data.SqlClient;
using McMaster.Extensions.CommandLineUtils;
using System.Reflection;

namespace poco_generator
{
    class Program
    {
        static void Main(string[] args)
        {
            var cliApp = new CommandLineApplication();
            cliApp.Name = "poco-generator";
            var tableName = cliApp
                .Argument("table", "Table Name", false);
            var connectionString = cliApp
                .Option("-c|--connstr", "Connection string. Required.", CommandOptionType.SingleValue);
            var retainUnderscore = cliApp
                .Option("-_", "Retain underscores", CommandOptionType.NoValue);
            var className = cliApp
                .Option("--class-name", "Override the class name", CommandOptionType.SingleValue);
            cliApp.HelpOption("-h|--help");

            
            cliApp.OnExecute(() => {
                if (!connectionString.HasValue()) {
                    Console.Error.WriteLine("-c|--connstr is a required argument");
                }
                else {
                    var columns = GetColumns(connectionString.Value(), tableName.Value);
                    var typeMapper = GetTypeMapper();
                    var nameConverter = GetNameConverter(retainUnderscore.HasValue());

                    Generate(tableName.Value, columns, typeMapper, nameConverter, className.Value());
                }
            });
            cliApp.Execute(args);

        }

        static void Generate(string tableName, IEnumerable<SchemaColumn> columns, Func<string, Type> typeMapper, Func<string, string> nameConverter, string className = null) {

            var indent = 0;
            Action<string> line = (l) => Console.WriteLine(string.Concat(Enumerable.Repeat("  ", indent)) + l);

            className = className ?? nameConverter(tableName);
            if (className != tableName) {
                line($"[Table(\"{tableName}\")]");
            }
            line($"public class {className} {{");

            Action<SchemaColumn, IEnumerable<SchemaColumn>> recurseColumns = null;
            recurseColumns = (column, tail) => {
                indent++;
                var nativeType = typeMapper(column.SqlDataType);

                if (column.IsPrimaryKey) {
                    line($"[Key]");
                }
                if (nameConverter(column.Name) != column.Name) {
                    line($"[Column(\"{column.Name}\")]");
                }

                var isNullableChar = (string)null;
                if (column.IsNullable && nativeType.GetTypeInfo().IsValueType) {
                    isNullableChar = "?";
                }
                line($"public {nativeType.GetCSharpName()}{isNullableChar} {nameConverter(column.Name)} {{ get; set; }}");

                if (tail.Any()) line(""); // blank line seperator

                indent--;

                if (tail.Any()) recurseColumns(tail.First(), tail.Skip(1));
            };
            recurseColumns(columns.First(), columns.Skip(1));

            line("}");
        }

        static IEnumerable<SchemaColumn> GetColumns(string connectionString, string tableName) {
            const string sql = @"
                declare @PKs table (COLUMN_NAME varchar(255))

                insert into @PKs
                select COLUMN_NAME
                    from INFORMATION_SCHEMA.TABLE_CONSTRAINTS as Constraints
                    inner join INFORMATION_SCHEMA.CONSTRAINT_COLUMN_USAGE as Columns on Columns.CONSTRAINT_NAME = Constraints.CONSTRAINT_NAME
                    where Constraints.TABLE_NAME = @TableName
                        and Constraints.CONSTRAINT_TYPE = 'PRIMARY KEY'

                select COLUMN_NAME, IS_NULLABLE, DATA_TYPE, isnull((select 1 from @PKs where COLUMN_NAME = col.COLUMN_NAME),0) as IS_PK
                        
                from INFORMATION_SCHEMA.COLUMNS as col
                where TABLE_NAME = @TableName
                order by ORDINAL_POSITION
            ";
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();

                using (var command = new SqlCommand(sql) { Connection = conn }.WithParameter("@TableName", tableName)) 
                using (var reader = command.ExecuteReader())
                {
                    return reader.ReadList(rdr => new SchemaColumn() {
                        Name = rdr.GetString(0),
                        IsNullable = rdr.GetString(1) == "YES",
                        SqlDataType = rdr.GetString(2),
                        IsPrimaryKey = rdr.GetInt32(3) == 1
                    });
                }
            }
        }

        static Func<string, Type> GetTypeMapper() {
            var mappings = new Dictionary<string, Type>();
            mappings.Add("int", typeof(int));
            mappings.Add("decimal", typeof(decimal));
            mappings.Add("float", typeof(double));
            mappings.Add("varchar", typeof(string));
            mappings.Add("nvarchar", typeof(string));
            mappings.Add("text", typeof(string));
            mappings.Add("ntext", typeof(string));
            mappings.Add("bit", typeof(bool));
            mappings.Add("datetime", typeof(DateTime));
            mappings.Add("date", typeof(DateTime));
            mappings.Add("money", typeof(decimal));

            return (sqlName) => {
                if (mappings.ContainsKey(sqlName.ToLower())) {
                    return mappings[sqlName.ToLower()];
                }

                throw new Exception($"No type mapping exists for sql data type '{sqlName}'");
            };
        }

        public static Func<string, string> GetNameConverter(bool retainUnderscore) {
            var regex = new Regex($@"[A-Za-z0-9{ (retainUnderscore ? "_" : "") }]+", RegexOptions.Compiled);

            return source => 
                string.Concat(
                    regex.Matches(source)
                        .Cast<Match>()
                        .Select(x => x.Value.First().ToString().ToUpper() 
                            + string.Concat(x.Value.Skip(1)))
                );
        }
    }

    public class SchemaColumn {
        public string Name { get; set; }
        public bool IsNullable { get; set; }
        public string SqlDataType { get; set; }
        public bool IsPrimaryKey { get; set; }
    }

    public static class Extensions {
        public static SqlCommand WithParameter(this SqlCommand command, string name, object value) {
            command.Parameters.AddWithValue(name, value);

            return command;
        }

        public static IEnumerable<T> ReadList<T>(this SqlDataReader reader, Func<SqlDataReader, T> rowEntityFactory) {
            var list = new List<T>();

            while (reader.Read()) {
                list.Add(rowEntityFactory(reader));
            }

            return list;
        }

        public static string GetCSharpName(this Type t) {
            if (t.Equals(typeof(string))) return "string";
            if (t.Equals(typeof(int))) return "int";
            if (t.Equals(typeof(decimal))) return "decimal";
            if (t.Equals(typeof(double))) return "double";
            if (t.Equals(typeof(bool))) return "bool";

            return t.Name;
        }
    }
}
