# OrleansStrongConsistency
Demo of multi-grain transaction with data consistency for Microsoft Orleans

Required Reading
=======
Before taking a look at this code example, please follow the Microsoft Orleans Step-by-step tutorials, otherwise this project will not make any sense.

http://dotnet.github.io/orleans/Step-by-step-Tutorials/Minimal-Orleans-Application


Purpose
=======
I'm working on a game that has in-game currency.  I need to make reasonable guarantees that if a Orleans silo crashes mid-transaction that the state of the game can be restored to a consistent state.

Persisting state for single grain instance is an atomic operation (since writing an entry to Azure Table is an atomic operation).  So in cases where a transaction modifies two properties of the same grain (player trades currency for goods), there is no concern.  The player is always left in a consistent state because the entire transaction either succeeds or fails atomically.

The problem occurs when a currency transaction spans two grain instances.  If a currency transaction involves multiple grain instances, all instances would need to be persisted as one atomic operation.  Azure Table does support Entity Group Transactions(https://azure.microsoft.com/en-us/documentation/articles/storage-table-design-guide/), but in order to use this feature all entities involved must have the same partition key (with different row keys). It appears the Orleans AzureTableStorage Provider uses unique PartitionKeys for each Grain which maximizes scalability but means that no atomic transactions can be used to maintain strong consistency of the data.  This design choice makes a lot of sense, so I still need another solution.

The following codebase is meant to serve as an example of how to maintain consistency even though a silo can crash in-between Grain A and Grain B persisting states while a transaction is in flight between the two grains.  Essentially the first grain is persisted in a 'pending' state until it can confirm that the second grain has completed or rejected the transaction.

Features of this implementation:
- An easy to use base grain class.  Any grain that extends from it can be used in a transactions that guarantee data consistency.
- A transaction can be committed atomically across 2 or more grains.  If a failure occurs before the commit is complete, all grains safely rollback to the previous state.
- Cycles are avoided when two transactions are executing in parallel by locking grains in a deterministic order.
- In the optimistic case, N+1 writes are required to complete a transaction where N is the number of grains involved.
- If any grain returns false because it has an insufficient balance to complete the transaction, then the transaction is rolled back on the all grains.


Setup
=======
Install Visual Studio 2015 Community Edition.  Open "OrleansStrongConsistency.sln" and nuget should automatically install the Microsoft Orleans packages that are required.

Be sure to open "Host\OrleansConfiguration.xml" and insert the name of your Azure "Storage Account Name" and "Primary Access Key" in the following line:
                DataConnectionString="DefaultEndpointsProtocol=https;AccountName=YOURAZURESOTRAGEACCOUNTNAME;AccountKey=YOURAZURESTORAGEACCOUNTKEY" />

Hit F5 to run the Host which is a console application.  You should see two "Employees" trading Currency and Goods back and forth.  Feel free to kill the process mid-transaction, and restart it to see how the system recovers.

Change History
==============
Nov 26, 2015 - Original example with transaction code embedded in Employee class
Dev 26, 2015 - Made generic base class GrainStateWithTransfer which supports safe grain to grain transfer
Jan 9, 2016 - Improvements to the base class and some unit tests