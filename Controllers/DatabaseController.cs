using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Threading;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using WebDBBackup.Models;

namespace WebDBBackup.Controllers
{
    public class DatabaseController : Controller
    {
        private readonly IConfiguration _configuration;

        public DatabaseController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public ActionResult Index()
        {
            try
            {
                List<DatabaseModel> databases = ListDatabases();
                return View(databases);
            }
            catch (Exception ex)
            {
                // Log or handle the exception appropriately
                Console.WriteLine($"Error in Index: {ex.Message}");
                TempData["Message"] = "An error occurred while listing databases.";
                return RedirectToAction("Index");
            }
        }

        [HttpPost]
        public ActionResult BackupAndUpload(List<DatabaseModel> selectedDatabases)
        {
            try
            {
                // Check if any databases are selected
                if (selectedDatabases == null || selectedDatabases.Count == 0)
                {
                    TempData["Message"] = "No databases selected for backup.";
                    return RedirectToAction("Index");
                }

                // Create a list to store database names and drive links
                List<BackupInfoModel> backupInfoList = new List<BackupInfoModel>();

                foreach (var database in selectedDatabases)
                {
                    if (database.IsSelected)
                    {
                        if (database.Name == "tempdb") 
                        {
                            TempData["Message"] = "Cannot upload tempdb";
                            continue;
                        }
                        string backupFilePath = GenerateDatabaseBackup(database.Name);
                        string targetFolderId = "15SOp79YBt4ojf-SAWHasecuHu416NpNW";
                        string driveLink = UploadToGoogleDrive(backupFilePath, targetFolderId);

                        backupInfoList.Add(new BackupInfoModel
                        {
                            DatabaseName = database.Name,
                            DriveLink = driveLink
                        });
                    }
                }

                // Check if there is actual backup information to display
                if (backupInfoList.Count > 0)
                {
                    return View("BackupInfoView", backupInfoList);
                }
                else
                {
                    TempData["Message"] = "No databases selected for backup.";
                    return RedirectToAction("Index");
                }

            }
            catch (Exception ex)
            {
                // Log or handle the exception appropriately
                Console.WriteLine($"Error in BackupAndUpload: {ex.Message}");
                TempData["Message"] = $"An error occurred during backup and upload.\n{ex.Message}";
                return RedirectToAction("Index");
            }
        }

        public ActionResult RemoveBackupFiles()
        {
            try
            {
                string targetFolderId = "15SOp79YBt4ojf-SAWHasecuHu416NpNW";

                RemoveBackupFilesFromFolder(targetFolderId);

                TempData["Message"] = "All backup files removed from Google Drive folder.";

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                // Log or handle the exception appropriately
                Console.WriteLine($"Error in RemoveBackupFiles: {ex.Message}");
                TempData["Message"] = $"An error occurred while removing backup files. {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        private List<DatabaseModel> ListDatabases()
        {
            List<DatabaseModel> databases = new List<DatabaseModel>();

            string connectionString = _configuration.GetConnectionString("DefaultConnection");

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT name FROM master.sys.databases";

                using (var command = new SqlCommand(query, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            databases.Add(new DatabaseModel { Name = reader["name"].ToString() });
                        }
                    }
                }
            }

            return databases;
        }

        private string GenerateDatabaseBackup(string databaseName)
        {
            string backupFolderPath = "E:\\Backup";
            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            string backupFilePath = Path.Combine(backupFolderPath, $"{databaseName}_Backup.bak");
            string zipFilePath = Path.Combine(backupFolderPath, $"{databaseName}_Backup.zip");

            // Check if the backup folder exists, and create it if not
            if (!Directory.Exists(backupFolderPath))
            {
                Directory.CreateDirectory(backupFolderPath);
            }

            // Backup the database
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string backupQuery = $"BACKUP DATABASE [{databaseName}] TO DISK = '{backupFilePath}'";
                using (var command = new SqlCommand(backupQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }

            // Zip the backup file
            using (var zip = System.IO.Compression.ZipFile.Open(zipFilePath, System.IO.Compression.ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(backupFilePath, Path.GetFileName(backupFilePath));
            }

            // Delete the original backup file
            System.IO.File.Delete(backupFilePath);

            return zipFilePath;
        }

        private UserCredential GetGoogleDriveCredentials()
        {
            UserCredential credential;

            // Load credentials from a file
            using (var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
            {
                string credPath = "token.json";

                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.Load(stream).Secrets,
                    new[] { DriveService.Scope.Drive },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
            }

            return credential;
        }

        private string UploadToGoogleDrive(string filePath, string targetFolderId)
        {
            Console.WriteLine("File Path: " + filePath);

            // Get Google Drive credentials
            var credential = GetGoogleDriveCredentials();

            // Create Drive API service
            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Google Drive API",
            });

            // Upload the file
            var fileMetadata = new Google.Apis.Drive.v3.Data.File()
            {
                Name = Path.GetFileName(filePath),
                Parents = new List<string> { targetFolderId },
            };

            FilesResource.CreateMediaUpload request;
            using (var stream = new FileStream(filePath, FileMode.Open))
            {
                request = service.Files.Create(fileMetadata, stream, "application/octet-stream");
                request.Upload();
            }

            var file = request.ResponseBody;
            System.IO.File.Delete(filePath);
            var driveLink = $"https://drive.google.com/open?id={file.Id}\n";

            Console.WriteLine($"File uploaded to Google Drive. Link: {driveLink}");

            return driveLink;
        }


        private void RemoveBackupFilesFromFolder(string folderId)
        {
            var credential = GetGoogleDriveCredentials();

            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Google Drive API",
            });

            var fileListRequest = service.Files.List();
            fileListRequest.Q = $"'{folderId}' in parents";
            var files = fileListRequest.Execute().Files;

            foreach (var file in files)
            {
                service.Files.Delete(file.Id).Execute();
                Console.WriteLine($"File {file.Name} deleted from Google Drive.");
            }
        }
    }
}
