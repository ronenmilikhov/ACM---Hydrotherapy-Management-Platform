const fs = require('fs');
const path = require('path');

const filePath = path.join(__dirname, '..', '..', '..', 'Manager', 'ManagerSystemReports.js');
const content = fs.readFileSync(filePath, 'utf8');
const lines = content.split('\n');

for (let i = 149; i < 185; i++) {
  if (lines[i] !== undefined) {
    console.log(`${i + 1}: ${lines[i]}`);
  }
}
