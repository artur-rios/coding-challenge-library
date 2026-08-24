using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Library.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "library");

            migrationBuilder.CreateTable(
                name: "audit_event",
                schema: "library",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "book",
                schema: "library",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    isbn = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    total_copies = table.Column<int>(type: "integer", nullable: false),
                    available_copies = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_book", x => x.id);
                    table.CheckConstraint("ck_book_available_copies_non_negative", "available_copies >= 0");
                    table.CheckConstraint("ck_book_available_within_total", "available_copies <= total_copies");
                    table.CheckConstraint("ck_book_total_copies_non_negative", "total_copies >= 0");
                });

            migrationBuilder.CreateTable(
                name: "idempotency_record",
                schema: "library",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    response_body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_record", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "library_user",
                schema: "library",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_library_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "loan",
                schema: "library",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    public_id = table.Column<Guid>(type: "uuid", nullable: false),
                    book_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    loaned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    returned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan", x => x.id);
                    table.CheckConstraint("ck_loan_status_timestamps", "(status = 1 AND returned_at IS NULL AND cancelled_at IS NULL)\nOR (status = 2 AND returned_at IS NOT NULL AND cancelled_at IS NULL)\nOR (status = 3 AND cancelled_at IS NOT NULL AND returned_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_loan_book_book_id",
                        column: x => x.book_id,
                        principalSchema: "library",
                        principalTable: "book",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loan_library_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "library",
                        principalTable: "library_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_correlation_id",
                schema: "library",
                table: "audit_event",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_entity",
                schema: "library",
                table: "audit_event",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_occurred_at",
                schema: "library",
                table: "audit_event",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_event_public_id",
                schema: "library",
                table: "audit_event",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_book_public_id",
                schema: "library",
                table: "book",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_book_isbn",
                schema: "library",
                table: "book",
                column: "isbn",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_record_expires_at",
                schema: "library",
                table: "idempotency_record",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_record_key",
                schema: "library",
                table: "idempotency_record",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_library_user_public_id",
                schema: "library",
                table: "library_user",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_library_user_email",
                schema: "library",
                table: "library_user",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_book_loaned_at",
                schema: "library",
                table: "loan",
                columns: new[] { "book_id", "loaned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_loan_public_id",
                schema: "library",
                table: "loan",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_user_loaned_at",
                schema: "library",
                table: "loan",
                columns: new[] { "user_id", "loaned_at" });

            migrationBuilder.CreateIndex(
                name: "ux_loan_active_book_user",
                schema: "library",
                table: "loan",
                columns: new[] { "book_id", "user_id" },
                unique: true,
                filter: "status = 1");

            // Hand-written because EF Core cannot express either of these.
            //
            // pg_trgm plus GIN indexes over LOWER(title) and LOWER(author): the catalogue listing
            // filters with a case-insensitive substring match, which PostgreSQL plans as
            // LOWER(col) LIKE '%...%'. A B-tree index cannot serve a leading-wildcard LIKE at all, so
            // without these every filtered listing is a sequential scan over the whole catalogue -
            // which is precisely the query the Redis cache sits in front of, and the one that has to
            // stay affordable when the cache misses.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_book_title_trgm ON library.book
                USING gin (LOWER(title) gin_trgm_ops);
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_book_author_trgm ON library.book
                USING gin (LOWER(author) gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The indexes go with the table below, so only the extension needs undoing explicitly -
            // and it is left in place on purpose: pg_trgm may be shared with something else in the
            // same database, and dropping an extension somebody else installed is not this
            // migration's call.
            migrationBuilder.Sql("DROP INDEX IF EXISTS library.ix_book_title_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS library.ix_book_author_trgm;");
            migrationBuilder.DropTable(
                name: "audit_event",
                schema: "library");

            migrationBuilder.DropTable(
                name: "idempotency_record",
                schema: "library");

            migrationBuilder.DropTable(
                name: "loan",
                schema: "library");

            migrationBuilder.DropTable(
                name: "book",
                schema: "library");

            migrationBuilder.DropTable(
                name: "library_user",
                schema: "library");
        }
    }
}
