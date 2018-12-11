package com.horine.emailAttachmentDownloader;

import com.sun.mail.util.BASE64DecoderStream;

import java.io.*;
import java.util.Date;
import java.util.Vector;

import static java.lang.Integer.parseInt;
import static java.lang.Integer.toHexString;

public class ImageSaver {

    String folderPath;

    public ImageSaver(String folderPath) {
        this.folderPath = folderPath;
    }

    public void saveImage(BASE64DecoderStream picture, String filename, String filetype) throws IOException {
        Vector<byte[]> newFile = new Vector<byte[]>();
        Vector<Integer> newFileSizes = new Vector<Integer>();
        byte[] buffer = new byte[1024];
        int bytesRead;

        while ((bytesRead = picture.read(buffer)) != -1) {
            newFileSizes.add(bytesRead);
            newFile.addElement(buffer);
        }
        picture.close();

        //String newfilename = checkDuplicate(filename,newFile);
        //if (newfilename == null){   //file already exists, and data matches exactly
        //    return;
        //}

        File file = new File(folderPath + "/" + filename);
        FileOutputStream fos = new FileOutputStream(file);

        System.out.println("Wrote file: " + filename);
        for(int i = 0; i < newFile.size(); i++){
                fos.write(newFile.get(i),0,newFileSizes.get(i));
        }

        /*
        while((bytesRead = picture.read(buffer)) != -1){
            fos.write(buffer, 0, bytesRead);
        }
        */

        fos.close();
    }

    private String checkDuplicate(String filename, Vector<byte[]> newFileData){
        System.out.println("CHECK DUPLICATE: " + filename);
        String newFilename = filename;
        File[] files = new File(folderPath).listFiles();
        for (int i = 0; i < files.length; i++) {
            String[] filePath = files[i].getAbsolutePath().split("/");
            //System.out.println("ALT FILE: " + filePath[filePath.length-1]);
            if (filename.equals(filePath[filePath.length - 1])) {
                System.out.println("DUPLICATE: " + filename);
                //files have same filename, check to see if data is the same
                /*
                if (checkDataSimilarity(newFileData, new File(files[i].getAbsolutePath())) == true) {
                    System.out.println(filename + ": FILES ARE THE SAME");
                    return null;    //if the files are the same, return null
                }
                */
                System.out.println(filename + ": FILES ARE DIFFERENT");
                //add parentheses and try again
                int lastClose = filename.lastIndexOf(")");
                int lastOpen = filename.lastIndexOf("(");
                if (lastClose != -1 && lastOpen != -1 && lastClose - 1 > lastOpen) {
                    String paren = filename.substring(lastOpen + 1, lastClose);
                    //System.out.println("PAREN: " + paren);
                    int filenumber = Integer.parseInt(paren) + 1;
                    newFilename = filename.substring(0, lastOpen + 1)
                            + filenumber
                            + filename.substring(lastClose);
                } else {   //does not have set of parentheses
                    System.out.println(filename);
                    newFilename = filename.substring(0, filename.lastIndexOf("."))
                            + "(1)"
                            + filename.substring(filename.lastIndexOf("."));
                }
                newFilename = checkDuplicate(newFilename, newFileData);
            } else {System.out.println("NOT A DUPLICATE: " + filePath[filePath.length - 1]);}
        }
        return newFilename;
    }

    private boolean checkDataSimilarity(Vector<byte[]> newFileData, File existingFile){
        Vector<byte[]> existingFileData = new Vector<byte[]>();
        try {
            int bytesRead;
            byte[] buffer = new byte[1024];
            FileInputStream fis = new FileInputStream(existingFile);
            while((bytesRead = fis.read(buffer)) != -1){
                existingFileData.add(buffer);
            }
            fis.close();
        } catch (FileNotFoundException e) {
            e.printStackTrace();
        } catch (IOException e) {
            e.printStackTrace();
        }

        /*
        for (int i = 0; i < newFileData.size() && i < existingFileData.size(); i++) {
            String newData = "";
            String existingData = "";

            for (int j = 0; j < newFileData.get(i).length && j < existingFileData.get(i).length; j++){
                newData += newFileData.get(i)[j];
                existingData += existingFileData.get(i)[j];
            }
            System.out.println(newData + " : " + existingData);
        }
        */

        for (int i = 0; i < newFileData.size() && i < existingFileData.size(); i++){
            for (int j = 0; j < newFileData.get(i).length && j < existingFileData.get(i).length; j++) {
                if (newFileData.get(i)[j] == existingFileData.get(i)[j]) {continue;}
                else {return false;}
            }
        }
        return true;
    }
}
