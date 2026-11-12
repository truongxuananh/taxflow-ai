START TRANSACTION;

ALTER TABLE `ChatMessages` ADD `ContextJson` longtext CHARACTER SET utf8mb4 NULL;

UPDATE ChatMessages SET ContextJson = '{}' WHERE ContextJson IS NULL;

ALTER TABLE `ChatMessages` MODIFY COLUMN `ContextJson` longtext CHARACTER SET utf8mb4 NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261112041745_AddAssistantConversationContext', '8.0.30');

COMMIT;

