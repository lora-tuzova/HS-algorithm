using System.Collections.Concurrent;
using System;

Console.WriteLine("Please enter a valid integer number of nodes greater than 1:");
string? line = Console.ReadLine();
while (line == null)
{
    Console.WriteLine("Please enter a valid integer number of nodes greater than 1:");
    line = Console.ReadLine();
}

int nodeNumber = Int32.Parse(line);
HS.ExecuteElection(nodeNumber);

class Message
{
    public int Id { get; init; }
    public bool Direction { get; set; }
    public double Steps { get; set; }
    public bool isLeaderChosen { get; set; } = false;

    public Message(int id, bool direction, double steps)
    {
        Id = id;
        Direction = direction;
        Steps = steps;
    }
}

class Node
{
    public int Id { get; init; }
    public bool IsActive { get; set; } = true;
    public int Phase { get; set; } = 0;
    public int LeftNeighbour { get; init; }
    public int RightNeighbour { get; init; }
    public int MessagesBack { get; set; } = 0;

    public Node (int id, int left, int right)
    {
        Id = id;
        LeftNeighbour = left;
        RightNeighbour = right;
    }
}

class HS
{
    private static Semaphore _gate;
    private static int gateCounter = 0;
    private static System.Threading.ReaderWriterLock _lock = new System.Threading.ReaderWriterLock();

    private static List<ConcurrentQueue<Message>> mailboxes;
    private static List<Node> nodes;

    private static bool leaderChosen = false;
    private static int messageCounter = 0;
    

    public static void ExecuteElection (int nodeNumber) // Synchronising the nodes at each step
    {
        _gate = new Semaphore(0, nodeNumber);

        nodes = new List<Node>();
        nodes.Add(new Node(1, nodeNumber, 2));

        for (int i = 2; i < nodeNumber; i++)
            nodes.Add(new Node(i, i-1, i+1));

        nodes.Add(new Node(nodeNumber, nodeNumber-1, 1));

        mailboxes = new List<ConcurrentQueue<Message>>();
        for (int i = 0; i < nodeNumber; i++)
            mailboxes.Add(new ConcurrentQueue<Message>());

        List<Thread> threads = new List<Thread>();
        for (int i = 0; i < nodeNumber; i++)
        {
            threads.Add(new Thread(new ParameterizedThreadStart(ExecuteAlgorithm)));
        }

        for (int i= 0; i < nodeNumber; i++)
            threads[i].Start(i+1);

        int counter = 0; // Phase counter
        while (!leaderChosen) // Syncing logic
        {
            double steps = Math.Pow(2, counter);
            
            while (gateCounter != nodeNumber && !leaderChosen)
                Thread.Sleep(10);
            gateCounter = 0;

            for (int j = 0; j < nodeNumber; j++)
                _gate.Release();

            // Sync after each mailbox emptying until the mail has run out
            for (int k = 0; k < steps * 2; k++)
            {
                while (gateCounter != nodeNumber && !leaderChosen)
                    Thread.Sleep(10);
                gateCounter = 0;
                    
                if (!leaderChosen)
                    for (int j = 0; j < nodeNumber; j++)
                        _gate.Release();
            }
            
            counter++;
        }

        for (int i = 0; i < nodeNumber; i++) // Waiting for all the nodes to finish and printing statistics
            if (threads[i].ThreadState == ThreadState.Running)
                threads[i].Join();

        Console.WriteLine("Total messages sent: " + messageCounter);
        Console.WriteLine("Maximum phase reached: " + (counter-1));
    }

    private static void ExecuteAlgorithm(object num) // Continuously executing synchronised phases
    {
        
        Node thisNode = nodes[(int)num - 1];
        
        while (!leaderChosen){
            ProcessMessages(thisNode); // One phase per call
        }
    }

    private static void ProcessMessages (Node thisNode)
    {
        int localMessagesCount = 0;
        double distance = Math.Pow(2, thisNode.Phase);
        if (thisNode.IsActive && !leaderChosen) // Sending current node's id both ways if it's active
        {
            mailboxes[thisNode.LeftNeighbour - 1].Enqueue(new Message(thisNode.Id, false, distance));
            mailboxes[thisNode.RightNeighbour - 1].Enqueue(new Message(thisNode.Id, true, distance));
            localMessagesCount += 2;
            Console.WriteLine($"Id sent from {thisNode.Id}");
        }

        lock (_lock) // Waiting for other active nodes to finish broadcasting ids
        {
            gateCounter++;
        }
        _gate.WaitOne();

        for (int j = 0; j < distance * 2; j++) // Waiting for any messages coming from any direction
        {
            while (mailboxes[thisNode.Id - 1].Any()) // While current node's mailbox isn't empty, processing mail
            {
                mailboxes[thisNode.Id - 1].TryDequeue(out Message mess);

                // Subtracting one step after receiving the message
                mess.Steps--;

                // If message is about the end of election, passing it along clockwise
                if (mess.isLeaderChosen && mess.Id != thisNode.Id && mess.Id != thisNode.RightNeighbour)
                {
                    mailboxes[thisNode.RightNeighbour - 1].Enqueue(new Message(mess.Id, mess.Direction, mess.Steps) { isLeaderChosen = true });
                    localMessagesCount++;
                    leaderChosen = true;
                    lock (_lock)
                    {
                        messageCounter += localMessagesCount;
                    }
                    return;
                }

                // If steps = 0, reversing direction, subtracting 1 step for negative values and sending back
                else if (mess.Steps == 0 && mess.Id != thisNode.Id)
                {
                    if (mess.Id > thisNode.Id)
                        thisNode.IsActive = false;
                    if (mess.Direction)
                        mailboxes[thisNode.LeftNeighbour - 1].Enqueue(new Message(mess.Id, !mess.Direction, -1));
                    else
                        mailboxes[thisNode.RightNeighbour - 1].Enqueue(new Message(mess.Id, !mess.Direction, -1));
                    localMessagesCount++;
                }


                // If steps < 0 or current node's id is smaller, passing along unchanged
                else if ((mess.Steps < 0 && mess.Id != thisNode.Id) || mess.Id > thisNode.Id)
                {
                    if (mess.Id > thisNode.Id)
                        thisNode.IsActive = false;
                    if (mess.Direction)
                        mailboxes[thisNode.RightNeighbour - 1].Enqueue(new Message(mess.Id, mess.Direction, mess.Steps));
                    else
                        mailboxes[thisNode.LeftNeighbour - 1].Enqueue(new Message(mess.Id, mess.Direction, mess.Steps));
                    localMessagesCount++;
                }

                // If steps < 0 and current node's id matches, message came back to author
                else if (mess.Steps < 0 && mess.Id == thisNode.Id)
                    thisNode.MessagesBack++;

                // If current node's own id came back with non-negative steps, it is the winner
                else if (mess.Id == thisNode.Id && mess.Steps >= 0)
                {
                    mailboxes[thisNode.RightNeighbour - 1].Enqueue(new Message(mess.Id, mess.Direction, mess.Steps) { isLeaderChosen = true });
                    localMessagesCount++;
                    leaderChosen = true;
                    Console.WriteLine($"Node {thisNode.Id} is the leader");
                    lock (_lock)
                    {
                        messageCounter += localMessagesCount;
                    }
                    return;
                }

            }
            lock (_lock)
            {
                gateCounter++;
            }
            _gate.WaitOne();
        }
        // All messages are processed, end of the phase
        // If less than 2 messages came back, the node becomes inactive and stops broadcasting its id
        if (thisNode.MessagesBack < 2)
            thisNode.IsActive = false;

        thisNode.MessagesBack = 0;
        thisNode.Phase++;
        Console.WriteLine($"New phase {thisNode.Phase} started for node {thisNode.Id}");

        lock (_lock)
        {
            messageCounter += localMessagesCount;
        }

    }
}

