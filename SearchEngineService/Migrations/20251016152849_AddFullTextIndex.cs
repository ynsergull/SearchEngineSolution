using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SearchEngineService.Migrations
{
    /// <inheritdoc />
    public partial class AddFullTextIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE `Contents`
                ADD FULLTEXT INDEX `IX_Contents_FT` (`Title`, `Description`);
            """);
        }
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX `IX_Contents_FT` ON `Contents`;
            """);
        }

    }
}
