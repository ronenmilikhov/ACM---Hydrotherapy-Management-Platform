const fs = require('fs');
const path = require('path');

const filePath = path.join(__dirname, '..', '..', '..', 'ServerSide', 'ReactServerSide', 'DAL', 'DBServices.cs');
const content = fs.readFileSync(filePath, 'utf8');
const lines = content.split('\n');

lines.forEach((line, idx) => {
  if (line.includes('AmazonDynamoDBClient') || line.includes('DynamoDb') || line.includes('ServiceUrl')) {
    console.log(`${idx + 1}: ${line.trim()}`);
  }
});
