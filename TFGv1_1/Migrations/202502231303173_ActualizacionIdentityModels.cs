namespace TFGv1_1.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class ActualizacionIdentityModels : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.SensorLogFiles", "SensorId", "dbo.Sensors");
            AddColumn("dbo.SensorLogFiles", "LogFileId", c => c.Int(nullable: false));
            AddForeignKey("dbo.SensorLogFiles", "SensorId", "dbo.Sensors", "SensorID", cascadeDelete: true);
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.SensorLogFiles", "SensorId", "dbo.Sensors");
            DropColumn("dbo.SensorLogFiles", "LogFileId");
            AddForeignKey("dbo.SensorLogFiles", "SensorId", "dbo.Sensors", "SensorID");
        }
    }
}
