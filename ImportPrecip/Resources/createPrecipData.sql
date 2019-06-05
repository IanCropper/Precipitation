USE [JbaMaster]
GO

/****** Object:  Table [dbo].[PrecipData]    Script Date: 01/06/2019 22:30:20 ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[PrecipData](
	[Xref] [int] NOT NULL,
	[Yref] [int] NOT NULL,
	[Date] [date] NOT NULL,
	[Value] [int] NOT NULL,
 CONSTRAINT [PK_PrecipData] PRIMARY KEY CLUSTERED 
(
	[Xref] ASC,
	[Yref] ASC,
	[Date] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
GO


