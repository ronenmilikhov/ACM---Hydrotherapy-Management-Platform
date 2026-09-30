const fs = require('fs');
const path = require('path');

const filePath = path.join(__dirname, '..', '..', '..', 'ServerSide', 'ReactServerSide', 'DAL', 'DBServices.cs');
const content = fs.readFileSync(filePath, 'utf8');
const lines = content.split('\n');

for (let i = 8039; i < 8095; i++) {
  if (lines[i] !== undefined) {
    console.log(`${i + 1}: ${lines[i]}`);
  }
}
