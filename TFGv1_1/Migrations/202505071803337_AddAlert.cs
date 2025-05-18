namespace TFGv1_1.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class AddAlert : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Alerts", "ThresholdRange", c => c.String(nullable: false));
            DropColumn("dbo.Alerts", "ThresholdValue");
        }
        
        public override void Down()
        {
            AddColumn("dbo.Alerts", "ThresholdValue", c => c.Double(nullable: false));
            DropColumn("dbo.Alerts", "ThresholdRange");
        }
    }
}
