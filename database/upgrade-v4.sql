START TRANSACTION;

ALTER TABLE `Warehouses` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Users` ADD `AuthVersion` int NOT NULL DEFAULT 0;

ALTER TABLE `Users` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `TaxRecords` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Suppliers` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `StockTransactions` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Roles` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Reports` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Receivables` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Products` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Payments` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Payables` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Orders` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `OrderItems` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `OrderItems` ADD `UnitCost` decimal(18,2) NOT NULL DEFAULT 0.0;

ALTER TABLE `OCRCorrections` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Invoices` ADD `AccountingReviewed` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `Invoices` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `InvoiceItems` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `IncomeTransactions` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `ExpenseTransactions` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Documents` ADD `ConfirmedInvoiceId` char(36) COLLATE ascii_general_ci NULL;

ALTER TABLE `Documents` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Customers` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `CompanyUsers` ADD `IsActive` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `CompanyUsers` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `CompanyUsers` ADD `RoleName` varchar(40) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

ALTER TABLE `Companies` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `Categories` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `BankTransactions` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `BankAccounts` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `AuditLogs` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `AIAnalyses` ADD `Revision` bigint NOT NULL DEFAULT 0;

ALTER TABLE `AIAlerts` ADD `Revision` bigint NOT NULL DEFAULT 0;

CREATE TABLE `ChatMessages` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `CompanyId` char(36) COLLATE ascii_general_ci NOT NULL,
    `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
    `Role` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `Text` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Source` varchar(250) CHARACTER SET utf8mb4 NULL,
    `Revision` bigint NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_ChatMessages` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `CompanyInvitations` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `CompanyId` char(36) COLLATE ascii_general_ci NOT NULL,
    `Email` varchar(160) CHARACTER SET utf8mb4 NOT NULL,
    `RoleName` varchar(40) CHARACTER SET utf8mb4 NOT NULL,
    `InvitedByUserId` char(36) COLLATE ascii_general_ci NOT NULL,
    `ExpiresAt` datetime(6) NOT NULL,
    `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `Revision` bigint NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_CompanyInvitations` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `OperationReceipts` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `CompanyId` char(36) COLLATE ascii_general_ci NOT NULL,
    `UserId` char(36) COLLATE ascii_general_ci NOT NULL,
    `Key` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `RequestHash` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `ResponseJson` longtext CHARACTER SET utf8mb4 NOT NULL,
    `StatusCode` int NOT NULL,
    `Revision` bigint NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_OperationReceipts` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE `OrderReturns` (
    `Id` char(36) COLLATE ascii_general_ci NOT NULL,
    `CompanyId` char(36) COLLATE ascii_general_ci NOT NULL,
    `OrderId` char(36) COLLATE ascii_general_ci NOT NULL,
    `RefundedAmount` decimal(18,2) NOT NULL,
    `ClearedDebt` decimal(18,2) NOT NULL,
    `Reason` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `CreatedByUserId` char(36) COLLATE ascii_general_ci NOT NULL,
    `Revision` bigint NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_OrderReturns` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE INDEX `IX_ChatMessages_CompanyId_UserId_CreatedAt` ON `ChatMessages` (`CompanyId`, `UserId`, `CreatedAt`);

CREATE INDEX `IX_CompanyInvitations_CompanyId_Email_Status` ON `CompanyInvitations` (`CompanyId`, `Email`, `Status`);

CREATE UNIQUE INDEX `IX_OperationReceipts_CompanyId_UserId_Key` ON `OperationReceipts` (`CompanyId`, `UserId`, `Key`);

CREATE UNIQUE INDEX `IX_OrderReturns_OrderId` ON `OrderReturns` (`OrderId`);

UPDATE CompanyUsers cu INNER JOIN Users u ON u.Id = cu.UserId INNER JOIN Roles r ON r.Id = u.RoleId SET cu.RoleName = r.Name, cu.IsActive = u.IsActive;

UPDATE OrderItems oi INNER JOIN Products p ON p.Id = oi.ProductId SET oi.UnitCost = p.PurchasePrice;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261112034119_CompletionSecurityAndLedger', '8.0.30');

COMMIT;

