const fs = require('fs');
const path = require('path');

const filePath = path.join(__dirname, '..', '..', '..', 'ServerSide', 'ReactServerSide', 'Controllers', 'InstructorController.cs');
const content = fs.readFileSync(filePath, 'utf8');
const lines = content.split('\n');

for (let i = 1340; i < 1400; i++) {
  console.log(`${i + 1}: ${lines[i]}`);
}
