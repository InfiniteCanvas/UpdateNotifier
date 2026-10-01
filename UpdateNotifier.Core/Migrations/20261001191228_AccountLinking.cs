using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateNotifier.Migrations
{
    /// <inheritdoc />
    public partial class AccountLinking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Accounts / WebAccounts / WebSessions / LinkCodes are brand-new tables: keep the
            // scaffolded operations for them verbatim.

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    AccountId = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.AccountId);
                });

            migrationBuilder.CreateTable(
                name: "LinkCodes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkCodes", x => x.Code);
                    table.ForeignKey(
                        name: "FK_LinkCodes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebAccounts",
                columns: table => new
                {
                    WebAccountId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, collation: "NOCASE"),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebAccounts", x => x.WebAccountId);
                    table.ForeignKey(
                        name: "FK_WebAccounts_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebSessions",
                columns: table => new
                {
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebSessions", x => x.TokenHash);
                    table.ForeignKey(
                        name: "FK_WebSessions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Hash",
                table: "Accounts",
                column: "Hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinkCodes_AccountId",
                table: "LinkCodes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_WebAccounts_AccountId",
                table: "WebAccounts",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebAccounts_Username",
                table: "WebAccounts",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebSessions_AccountId",
                table: "WebSessions",
                column: "AccountId");

            // Users and Watchlist are re-keyed (Users.UserId no longer owns the watchlist;
            // Accounts do) and Users.Hash rotates to a random value, so both tables are rebuilt
            // by hand in ONE raw block. No scaffolded operations against them may survive:
            // EF's SQLite rebuild machinery would wrap them in PRAGMA wrangling that splits or
            // breaks the migration transaction. Likewise NO hand-written PRAGMA statements and
            // NO suppressTransaction here - both are known-broken paths inside a migration.
            //
            // The DDL below mirrors the final model exactly:
            //   Users        -> UserId (PK, autoincrement), DiscordUsername (TEXT NULL), AccountId (FK -> Accounts, cascade, unique)
            //   Watchlist    -> composite PK (AccountId, GameId), FKs -> Accounts + Games (cascade)
            //   hash rotation -> hex(randomblob(20)) = 40 uppercase hex chars, like Convert.ToHexString(RandomNumberGenerator.GetBytes(20))
            //   CreatedAt    -> datetime('now') produces TEXT EF can parse
            migrationBuilder.Sql(@"
DELETE FROM ""Watchlist"" WHERE ""UserId"" NOT IN (SELECT ""UserId"" FROM ""Users"");
DROP INDEX ""IX_Watchlist_GameId"";
INSERT INTO ""Accounts"" (""AccountId"", ""Hash"", ""CreatedAt"")
    SELECT ""UserId"", hex(randomblob(20)), datetime('now') FROM ""Users"";
CREATE TABLE ""Users_new"" (
    ""UserId"" INTEGER NOT NULL CONSTRAINT ""PK_Users"" PRIMARY KEY AUTOINCREMENT,
    ""DiscordUsername"" TEXT NULL,
    ""AccountId"" INTEGER NOT NULL,
    CONSTRAINT ""FK_Users_Accounts_AccountId"" FOREIGN KEY (""AccountId"") REFERENCES ""Accounts"" (""AccountId"") ON DELETE CASCADE
);
CREATE TABLE ""Watchlist_new"" (
    ""AccountId"" INTEGER NOT NULL,
    ""GameId"" INTEGER NOT NULL,
    CONSTRAINT ""PK_Watchlist"" PRIMARY KEY (""AccountId"", ""GameId""),
    CONSTRAINT ""FK_Watchlist_Accounts_AccountId"" FOREIGN KEY (""AccountId"") REFERENCES ""Accounts"" (""AccountId"") ON DELETE CASCADE,
    CONSTRAINT ""FK_Watchlist_Games_GameId"" FOREIGN KEY (""GameId"") REFERENCES ""Games"" (""GameId"") ON DELETE CASCADE
);
CREATE UNIQUE INDEX ""IX_Users_AccountId"" ON ""Users_new"" (""AccountId"");
INSERT INTO ""Users_new"" (""UserId"", ""AccountId"", ""DiscordUsername"") SELECT ""UserId"", ""UserId"", NULL FROM ""Users"";
INSERT INTO ""Watchlist_new"" (""AccountId"", ""GameId"") SELECT ""UserId"", ""GameId"" FROM ""Watchlist"";
DROP TABLE ""Watchlist"";
DROP TABLE ""Users"";
ALTER TABLE ""Users_new"" RENAME TO ""Users"";
ALTER TABLE ""Watchlist_new"" RENAME TO ""Watchlist"";
CREATE INDEX ""IX_Watchlist_GameId"" ON ""Watchlist"" (""GameId"");
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort reverse. Children before parents, no pragmas, no suppressTransaction.
            // WebAccounts/WebSessions/LinkCodes have no old-schema counterpart - that data is
            // discarded (the web frontend does not exist yet). Discord usernames are dropped too.

            migrationBuilder.DropTable(
                name: "LinkCodes");

            migrationBuilder.DropTable(
                name: "WebAccounts");

            migrationBuilder.DropTable(
                name: "WebSessions");

            // Rebuild Users/Watchlist in the old shape (Users.Hash recomputes via user_hash(),
            // which HashInterceptor registers on every connection). Must run BEFORE Accounts is
            // dropped: the current Users/Watchlist rows are the children of Accounts.
            //
            // Ordering hazard: SQLite's DROP TABLE runs an implicit DELETE, which fires ON DELETE
            // CASCADE on children. The new-shape Users table can only be dropped BEFORE
            // Watchlist_old exists (an empty child cascades to zero rows) - hence the tiny
            // AccountMap staging table captures the link while the source tables still exist.
            migrationBuilder.Sql(@"
DELETE FROM ""Watchlist"" WHERE ""AccountId"" NOT IN (SELECT ""AccountId"" FROM ""Users"");
DROP INDEX ""IX_Watchlist_GameId"";
CREATE TABLE ""AccountMap"" (
    ""UserId"" INTEGER NOT NULL PRIMARY KEY,
    ""AccountId"" INTEGER NOT NULL
);
INSERT INTO ""AccountMap"" (""UserId"", ""AccountId"") SELECT ""UserId"", ""AccountId"" FROM ""Users"";
CREATE TABLE ""Users_old"" (
    ""UserId"" INTEGER NOT NULL CONSTRAINT ""PK_Users"" PRIMARY KEY AUTOINCREMENT,
    ""Hash"" TEXT NOT NULL GENERATED ALWAYS AS (user_hash(cast(""UserId"" as text))) STORED
);
INSERT INTO ""Users_old"" (""UserId"") SELECT ""UserId"" FROM ""AccountMap"";
DROP TABLE ""Users"";
ALTER TABLE ""Users_old"" RENAME TO ""Users"";
CREATE TABLE ""Watchlist_old"" (
    ""UserId"" INTEGER NOT NULL,
    ""GameId"" INTEGER NOT NULL,
    CONSTRAINT ""PK_Watchlist"" PRIMARY KEY (""UserId"", ""GameId""),
    CONSTRAINT ""FK_Watchlist_Games_GameId"" FOREIGN KEY (""GameId"") REFERENCES ""Games"" (""GameId"") ON DELETE CASCADE,
    CONSTRAINT ""FK_Watchlist_Users_UserId"" FOREIGN KEY (""UserId"") REFERENCES ""Users"" (""UserId"") ON DELETE CASCADE
);
INSERT INTO ""Watchlist_old"" (""UserId"", ""GameId"")
    SELECT ""AccountMap"".""UserId"", ""Watchlist"".""GameId"" FROM ""Watchlist"" JOIN ""AccountMap"" ON ""AccountMap"".""AccountId"" = ""Watchlist"".""AccountId"";
DROP TABLE ""Watchlist"";
DROP TABLE ""AccountMap"";
ALTER TABLE ""Watchlist_old"" RENAME TO ""Watchlist"";
CREATE INDEX ""IX_Watchlist_GameId"" ON ""Watchlist"" (""GameId"");
");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
