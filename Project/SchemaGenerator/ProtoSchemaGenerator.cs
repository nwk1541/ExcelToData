using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Core;

namespace SchemaGenerator
{
    internal sealed class ProtoSchemaGenerator
    {
        private const string PROTO_PACKAGE_NAME = "data.local";
        private const string CSHARP_NAMESPACE = "Data.Local";
        private const string PROTOC_FILE_NAME = "protoc.exe";

        public void Generate(IReadOnlyList<TableData> tables)
        {
            string protoDirectoryPath = PathUtil.GetProtoDirectoryPath();
            string csharpOutputDirectoryPath = PathUtil.GetCSharpOutputDirectoryPath();
            string databaseOutputDirectoryPath = PathUtil.GetDatabaseOutputDirectoryPath();

            Directory.CreateDirectory(protoDirectoryPath);
            Directory.CreateDirectory(csharpOutputDirectoryPath);
            Directory.CreateDirectory(databaseOutputDirectoryPath);

            foreach (TableData table in tables)
            {
                string tableName = table.Schema.TableName;
                string protoFilePath = Path.Combine(protoDirectoryPath, $"{tableName}.proto");
                string csharpFilePath = Path.Combine(csharpOutputDirectoryPath, $"{tableName}.cs");

                File.WriteAllText(protoFilePath, CreateProtoSource(table.Schema), new UTF8Encoding(true));
                GenerateCSharp(protoFilePath, protoDirectoryPath, csharpOutputDirectoryPath);

                if (!File.Exists(csharpFilePath))
                {
                    throw new FileNotFoundException($"생성된 C# 파일을 찾을 수 없습니다. 경로: {csharpFilePath}");
                }

                ConvertToUtf8WithBom(csharpFilePath);
            }
        }

        private static string CreateProtoSource(TableSchema schema)
        {
            StringBuilder sourceBuilder = new();
            sourceBuilder.AppendLine("syntax = \"proto3\";");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine($"package {PROTO_PACKAGE_NAME};");
            sourceBuilder.AppendLine($"option csharp_namespace = \"{CSHARP_NAMESPACE}\";");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine($"message {schema.TableName}");
            sourceBuilder.AppendLine("{");

            foreach (ColumnSchema column in schema.Columns)
            {
                string repeatedPrefix = column.IsRepeated ? "repeated " : string.Empty;
                sourceBuilder.AppendLine($"    {repeatedPrefix}{GetProtoDataTypeName(column.DataType)} {column.Name} = {column.FieldNumber};");
            }

            sourceBuilder.AppendLine("}");

            return sourceBuilder.ToString();
        }

        private static void GenerateCSharp(string protoFilePath, string protoDirectoryPath, string csharpOutputDirectoryPath)
        {
            string protocFilePath = Path.Combine(AppContext.BaseDirectory, PROTOC_FILE_NAME);

            if (!File.Exists(protocFilePath))
            {
                throw new FileNotFoundException($"protoc 실행 파일을 찾을 수 없습니다. 경로: {protocFilePath}");
            }

            ProcessStartInfo startInfo = new(protocFilePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add($"--proto_path={protoDirectoryPath}");
            startInfo.ArgumentList.Add($"--csharp_out={csharpOutputDirectoryPath}");
            startInfo.ArgumentList.Add(protoFilePath);

            using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("protoc 프로세스를 시작하지 못했습니다.");
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string errorMessage = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                throw new InvalidOperationException($"C# 코드 생성에 실패했습니다. {errorMessage.Trim()}");
            }
        }

        private static void ConvertToUtf8WithBom(string filePath)
        {
            string source = File.ReadAllText(filePath);
            File.WriteAllText(filePath, source, new UTF8Encoding(true));
        }

        private static string GetProtoDataTypeName(ColumnDataType dataType)
        {
            return dataType switch
            {
                ColumnDataType.Int32 => "int32",
                ColumnDataType.Int64 => "int64",
                ColumnDataType.Float => "float",
                ColumnDataType.Double => "double",
                ColumnDataType.String => "string",
                ColumnDataType.Bool => "bool",
                _ => throw new InvalidOperationException($"지원하지 않는 Protocol Buffers 타입입니다. 타입: {dataType}")
            };
        }
    }
}
