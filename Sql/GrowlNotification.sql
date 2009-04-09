--
-- Script to create the SQL Server part of the objects for the Sql2Growl connector
-- Copyright (c) 2009, Daniel Knippers (daniel@knippers.com)
--
--
-- Setup part
-- 
-- NOTE!!! do not forget to change value for YOUR_DATABASE 
--
USE YOUR_DATABASE
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS( SELECT * FROM sys.databases WHERE is_broker_enabled = 1 AND name = 'YOUR_DATABASE' )
   ALTER DATABASE [YOUR_DATABASE] SET ENABLE_BROKER;
GO
IF USER_ID('GrowlUser') IS NULL
BEGIN
   CREATE LOGIN [GrowlUser] WITH PASSWORD='LPHgeLAaC6YoE1gUfcl9IPZV', 
      DEFAULT_DATABASE=[YOUR_DATABASE], DEFAULT_LANGUAGE=[us_english], 
      CHECK_EXPIRATION=OFF, CHECK_POLICY=OFF
      
   CREATE USER [GrowlUser] FOR LOGIN [GrowlUser] WITH DEFAULT_SCHEMA=[Growl]
END
GO
IF SCHEMA_ID('Growl') IS NULL 
   EXEC('CREATE SCHEMA [Growl]');
GO
--
-- Drop old objects (if they exist)
--
IF EXISTS( SELECT NAME 
             FROM sys.services
            WHERE NAME = '//Growl/NotificationService' )
BEGIN
   DROP SERVICE [//Growl/NotificationService];
END
GO
IF OBJECT_ID('Growl.NotificationQueue') IS NOT NULL
   DROP QUEUE Growl.NotificationQueue;
GO
IF EXISTS( SELECT NAME 
             FROM sys.service_contracts
            WHERE NAME = '//Growl/NotificationContract' )
BEGIN
   DROP CONTRACT [//Growl/NotificationContract];
END
GO
IF EXISTS( SELECT NAME 
             FROM sys.service_message_types
            WHERE NAME = '//Growl/NotificationMessage' )
BEGIN
   DROP MESSAGE TYPE [//Growl/NotificationMessage];
END
GO
--
-- Create objects
--
CREATE MESSAGE TYPE
    [//Growl/NotificationMessage]
    VALIDATION = WELL_FORMED_XML; 
GO
CREATE CONTRACT 
   [//Growl/NotificationContract]
   (
      [//Growl/NotificationMessage] 
      SENT BY ANY
   );
GO
CREATE QUEUE Growl.NotificationQueue WITH STATUS = ON, RETENTION = OFF;
GO
CREATE SERVICE [//Growl/NotificationService] ON 
   QUEUE Growl.NotificationQueue ([//Growl/NotificationContract]);
GO
--
-- Create procedures
--
IF OBJECT_ID('Growl.spQueueNotification') IS NOT NULL
   DROP PROCEDURE [Growl].[spQueueNotification];
GO
--
-- Procedure to be called from T-SQL (e.g. in a trigger)
-- The Password, Host and Port parameters are optional and only
-- need to be set if you want to sent notifications to Growl servers
-- other then the server running the Sql2Growl connector
-- The ClearCachedObjects is a special parameter, if set to 1 will instruct
-- the Sql2Growl service to flush all objects in cache and start over
--
CREATE PROCEDURE [Growl].[spQueueNotification]
(
   @Application NVARCHAR(100),
   @Type NVARCHAR(100),
   @Title NVARCHAR(200),
   @Message NVARCHAR(2000),
   @Icon VARCHAR(100) = NULL,
   @Password NVARCHAR(100) = NULL,
   @Host VARCHAR(100) = NULL,
   @Port INT = 23053,
   @ClearCachedObjects BIT = 0
)
WITH EXECUTE AS OWNER
AS
   DECLARE @ConversationId UNIQUEIDENTIFIER;
   DECLARE @Xml XML;
BEGIN
   BEGIN DIALOG CONVERSATION @ConversationId
        FROM SERVICE [//Growl/NotificationService]
          TO SERVICE '//Growl/NotificationService'
          ON CONTRACT [//Growl/NotificationContract]
        WITH ENCRYPTION = OFF, LIFETIME = (5*60); -- 5 minutes 

   SET @Xml = (
      SELECT @ConversationId "NotificationID",
             @Application "Application",
             @Type "Type",
             @Title "Title",
             @Message "Message",
             ISNULL( @Icon, '' ) "Icon",
             ISNULL( @Password, '' ) "Password",
             ISNULL( @Host, '' ) "Host",
             ISNULL( @Port, 23053 ) "Port",
             CASE WHEN @ClearCachedObjects = 1 
                THEN 'True' 
                ELSE 'False' 
             END "ClearCachedObjects"
         FOR XML PATH('Growl'), TYPE 
   );

   SEND ON CONVERSATION @ConversationId 
      MESSAGE TYPE [//Growl/NotificationMessage]
      (
        @Xml
      );
      
   END CONVERSATION @ConversationId
       
END
GO
IF OBJECT_ID('Growl.spGetNextNotification') IS NOT NULL
   DROP PROCEDURE [Growl].[spGetNextNotification];
GO
--
-- Resultcursor contains NotificationID and Notification XML
-- in case the client does rollback 5 times you need to reenable queue:
-- ALTER QUEUE Growl.NotificationQueue WITH STATUS = ON
--
CREATE PROCEDURE [Growl].[spGetNextNotification]
(
   @TimeoutSec INT = 60
)
WITH EXECUTE AS OWNER
AS
   SET NOCOUNT ON
   DECLARE @Timeout INT
   DECLARE @ConversationId UNIQUEIDENTIFIER;
   DECLARE @Xml XML;
   DECLARE @MsgType NVARCHAR(128);
   DECLARE @StartTime DATETIME;
BEGIN

   SET @Timeout = @TimeoutSec * 1000;
   SET @StartTime = GETDATE();

   WHILE @Timeout >= 0
   BEGIN
      WAITFOR(
         RECEIVE TOP(1) 
                 @ConversationId = conversation_handle,
                 @MsgType = message_type_name, 
                 @Xml = message_body
            FROM Growl.NotificationQueue
      ), TIMEOUT @Timeout;

      -- if received message is Notification Message break out while loop
      --    
      IF @MsgType = '//Growl/NotificationMessage'
      BEGIN
         BREAK;
      END;

      -- extract the time we already waited from the waitfor timeout
      -- 
      SET @Timeout = (@TimeoutSec*1000) - DATEDIFF( MILLISECOND, @StartTime, GETDATE() );
   END;

   IF @Xml IS NOT NULL
   BEGIN
      -- set the result cursor
      -- 
      SELECT @ConversationId NotificationID,
             @Xml NotificationXml;

      RETURN 0;
   END
   ELSE
   BEGIN
      RETURN 0; 
   END;
END
GO
--
-- Grant execure permissions to GrowlUser
--
GRANT EXECUTE ON [Growl].[spQueueNotification] TO [GrowlUser];
GO
GRANT EXECUTE ON [Growl].[spGetNextNotification] TO [GrowlUser];
GO
--
-- To validate the code in the database you can use the following
-- statements. 
--
/*
EXEC [Growl].[spQueueNotification]
	  @Application = N'TestApp',
	  @Type = N'Test',
	  @Title = N'Test Title',
	  @Message = N'Test Message'

WAITFOR(
   RECEIVE TOP(2) 
           conversation_handle,
           message_type_name, 
           CONVERT( xml, message_body )
      FROM Growl.NotificationQueue
), TIMEOUT 1000;
*/
