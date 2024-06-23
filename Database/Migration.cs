using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace CS2_SimpleAdmin.Database;

public class Migration(Database database)
{
	public void ExecuteMigrations()
	{
		/*var migrationsDirectory = CS2_SimpleAdmin.Instance.ModuleDirectory + "/Database/Migrations";

		var files = Directory.GetFiles(migrationsDirectory, "*.sql")
							 .OrderBy(f => f);

		using var connection = database.GetConnection();

		// Create sb_migrations table if not exists
		//using var cmd = new MySqlCommand("""
		                                //             CREATE TABLE IF NOT EXISTS `sb_migrations` (
		                                //                 `id` INT PRIMARY KEY AUTO_INCREMENT,
		                                //                 `version` VARCHAR(255) NOT NULL
		                                //             );
		                                // """, connection);

		//cmd.ExecuteNonQuery();

		// Get the last applied migration version
		var lastAppliedVersion = GetLastAppliedVersion(connection);

		foreach (var file in files)
		{
			var version = Path.GetFileNameWithoutExtension(file);

			// Check if the migration has already been applied
			if (string.Compare(version, lastAppliedVersion, StringComparison.OrdinalIgnoreCase) <= 0) continue;
			var sqlScript = File.ReadAllText(file);

			using var cmdMigration = new MySqlCommand(sqlScript, connection);
			cmdMigration.ExecuteNonQuery();

			// Update the last applied migration version
			UpdateLastAppliedVersion(connection, version);

			CS2_SimpleAdmin._logger?.LogInformation($"Migration \"{version}\" successfully applied.");
		}*/
	}

	private static string GetLastAppliedVersion(MySqlConnection connection)
	{
		using var cmd = new MySqlCommand("SELECT `setting` FROM `sb_settings` WHERE `setting` = 'config.version' DESC LIMIT 1;", connection);
		var result = cmd.ExecuteScalar();
		return result?.ToString() ?? string.Empty;
	}

	private static void UpdateLastAppliedVersion(MySqlConnection connection, string version)
	{
		//using var cmd = new MySqlCommand("INSERT INTO `sb_migrations` (`version`) VALUES (@Version);", connection);
		//cmd.Parameters.AddWithValue("@Version", version);
		//cmd.ExecuteNonQuery();
	}
}
