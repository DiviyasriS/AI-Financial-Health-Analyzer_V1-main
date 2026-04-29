# AI Financial Health Analyzer - Backend

## Features Implemented
- Database schema using Entity Framework Core
- Transaction storage and retrieval APIs
- CSV upload and processing pipeline
- Data validation and cleaning during ingestion

## API Endpoints

POST /api/transaction/upload
- Upload CSV file
- Stores processed transactions

GET /api/transaction/{userId}
- Retrieve transactions for a user

## Tech Stack
- ASP.NET Core Web API
- Entity Framework Core
- SQL Server

## Next Steps
- Spending analysis module
- AI-based financial risk prediction using ML.NET
- Dashboard integration
