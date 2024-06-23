ALTER TABLE `sb_bans` CHANGE `player_name` `player_name` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL AFTER `id`;
ALTER TABLE `sb_mutes` CHANGE `player_name` `player_name` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL AFTER `id`;
ALTER TABLE `sb_admins` CHANGE `player_name` `player_name` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL AFTER `id`;
ALTER TABLE `sb_servers` CHANGE `hostname` `hostname` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci NULL DEFAULT NULL AFTER `id`;