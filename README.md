
# PlotMerge
Command Line Plot Merger and Converter (Poc1&lt;>Poc2) for Windows. Merges adjacent plot files and optionally converts to Poc2.

![alt text](https://raw.githubusercontent.com/JohnnyFFM/PlotMerge/master/plotMerge/plotMerge/plotmerge.png)

***
## In Depth Instructions - Windows 10 - Powershell

1. Extract plotMerge.exe anywhere you like. I'll be using D:\plotMerge.exe
2. Right-click the Windows Start Button (Lower left Taskbar) and select "Windows Powershell (Admin)"
3. Change to the directory plotMerge.exe was extracted for ease: `Set-Location D:\ <ENTER>`

  * Let's assume you have 10 x POC2 files in D:\ each containing 15000 nonces  
    (XXXXXXXX would be my Burst Numeric ID) 
       
       *XXXXXXXX_0_15000*  
       *XXXXXXXX_15000_15000*  
       *XXXXXXXX_30000_15000*  
       *etc...*  
       *XXXXXXXX_135000_15000*  
       
      Your total nonces are: 150,000 (135000 + 15000)  
  
PlotMerge reads consecutive files from the start file and works out the numbers from there. You just need to tell it how many nonces you want in the new file.
Just look at the nonce numbers and add them up, in my case I have 10 files all ending in 15000, so 10 x 15000 = 150000

The command I need is:
**`.\plotMerge.exe D:\XXXXXXXX_0_15000 D:\newPlots\XXXXXXXX_0_150000`**

You WILL need available space Double that of the files you are merging.
