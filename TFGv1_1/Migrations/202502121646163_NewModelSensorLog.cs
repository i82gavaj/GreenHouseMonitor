namespace TFGv1_1.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class NewModelSensorLog : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.SensorLogFiles",
                c => new
                    {
                        SensorId = c.Int(nullable: false),
                        FilePath = c.String(nullable: false, maxLength: 255),
                        CreationDate = c.DateTime(nullable: false),
                    })
                .PrimaryKey(t => t.SensorId)
                .ForeignKey("dbo.Sensors", t => t.SensorId)
                .Index(t => t.SensorId);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.SensorLogFiles", "SensorId", "dbo.Sensors");
            DropIndex("dbo.SensorLogFiles", new[] { "SensorId" });
            DropTable("dbo.SensorLogFiles");
        }
    }
}
