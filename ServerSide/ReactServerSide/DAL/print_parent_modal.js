const fs = require('fs');
const path = require('path');

const filePath = path.join(__dirname, '..', '..', '..', 'Manager', 'ManagerHomepage.js');
if (!fs.existsSync(filePath)) {
  console.log("ManagerHomepage.js not found");
  process.exit(1);
}

const content = fs.readFileSync(filePath, 'utf8');
const lines = content.split('\n');

for (let i = 2620; i < 2740; i++) {
  if (lines[i] !== undefined) {
    console.log(`${i + 1}: ${lines[i]}`);
  }
}
