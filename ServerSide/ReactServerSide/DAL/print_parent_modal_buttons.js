const fs = require('fs');
const path = require('path');

const filePath = path.join(__dirname, '..', '..', '..', 'Manager', 'ManagerHomepage.js');
const content = fs.readFileSync(filePath, 'utf8');
const lines = content.split('\n');

for (let i = 2739; i < 2800; i++) {
  if (lines[i] !== undefined) {
    console.log(`${i + 1}: ${lines[i]}`);
  }
}
