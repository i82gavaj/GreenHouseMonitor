using System.Data.Entity.Migrations;

public partial class UpdateUserGreenHouseRelationship : DbMigration
{
    public override void Up()
    {
        DropForeignKey("dbo.GreenHouses", "UserID", "dbo.AspNetUsers");
        DropIndex("dbo.GreenHouses", new[] { "UserID" });
        
        AlterColumn("dbo.GreenHouses", "UserID", c => c.String(nullable: false, maxLength: 128));
        CreateIndex("dbo.GreenHouses", "UserID");
        AddForeignKey("dbo.GreenHouses", "UserID", "dbo.AspNetUsers", "Id", cascadeDelete: true);
    }
    
    public override void Down()
    {
        DropForeignKey("dbo.GreenHouses", "UserID", "dbo.AspNetUsers");
        DropIndex("dbo.GreenHouses", new[] { "UserID" });
        
        AlterColumn("dbo.GreenHouses", "UserID", c => c.String(maxLength: 128));
        CreateIndex("dbo.GreenHouses", "UserID");
        AddForeignKey("dbo.GreenHouses", "UserID", "dbo.AspNetUsers", "Id");
    }
} 